"""Repeatable local performance evidence; never substitutes for physical tablet/Wi-Fi QA."""
import concurrent.futures
import getpass
import hashlib
import json
import math
import os
import platform
import secrets
import socket
import subprocess
import tempfile
import time
import urllib.request
import uuid
from datetime import date, timedelta, datetime, timezone
from pathlib import Path
import api_acceptance
from api_acceptance import Client, TODAY


def distribution(values, limit):
    assert len(values) >= 50, 'insufficient percentile samples'
    ordered = sorted(values)
    p95 = ordered[math.ceil(.95 * len(ordered)) - 1]
    return {'samples': len(values), 'p50Ms': round(ordered[math.ceil(.5 * len(ordered)) - 1], 2),
            'p95Ms': round(p95, 2), 'maxMs': round(max(values), 2), 'limitMs': limit, 'passed': p95 <= limit,
            'rawMs': [round(v, 2) for v in values], 'percentileMethod': 'nearest rank, ceil(0.95*n)'}


def main():
    root = Path(__file__).resolve().parent.parent
    env = os.environ.copy()
    suffix = uuid.uuid4().hex[:12]
    database = 'learning_fault_' + suffix
    env.update(PGHOST='127.0.0.1', PGPORT='55432', PGUSER=getpass.getuser(), PGDATABASE=database,
               PLAN_TEST_USERNAME='perf-fixture-' + suffix, PLAN_TEST_PASSWORD=secrets.token_hex(20))
    env['PATH'] = '/opt/homebrew/opt/postgresql@16/bin:' + env['PATH']
    connection = f'Host=127.0.0.1;Port=55432;Database={database};Username={env["PGUSER"]}'
    env.update(PERSISTENCE_TEST_CONNECTION=connection, ConnectionStrings__Learning=connection)
    dotnet = str(root / '.tools/dotnet/dotnet')
    child = None
    report = {'measuredAt': datetime.now(timezone.utc).isoformat(),
              'environment': {'os': platform.platform(), 'architecture': platform.machine(), 'cpuCount': os.cpu_count(),
                              'transport': 'loopback HTTP', 'serverBuild': 'Debug', 'realTabletWifi': False,
                              'serverDllSha256': hashlib.sha256((root/'src/server/bin/Debug/net10.0/Learning.Api.dll').read_bytes()).hexdigest(),
                              'sourceCommit': subprocess.check_output(['git','rev-parse','HEAD'],cwd=root,text=True).strip()},
              'workload': {'families': 1, 'measuredStudents': 3, 'unmeasuredSeedStudents': 1, 'rounds': 20, 'concurrentAnswerFlows': 3,
                           'apiActor': 'Parent', 'browserActor': 'Child', 'gradingCases': 'First encounter per KC Incorrect; later answers Correct',
                           'answerTime': 'actual server clock; scheduled dates are synthetic, not retention evidence'},
              'metrics': {}, 'limitations': ['开发机和模拟网络不证明实际平板/家庭Wi-Fi目标达标。',
                                            '20题样例库，不代表100候选上界或100–300题正式内容规模。',
                                            '非四周真实学习，不据此推算教育效果或长期可用性。']}
    plan_ms, answer_ms, projection_ms, today_ms = [], [], [], []
    subprocess.run(['createdb', database], env=env, check=True)
    with tempfile.TemporaryDirectory(prefix='learning-performance-', dir='/private/tmp') as tmp:
        env.update(DeletionLedger=str(Path(tmp) / 'students.txt'), FamilyDeletionLedger=str(Path(tmp) / 'families.txt'),
                   ExportDirectory=str(Path(tmp) / 'exports'))
        try:
            fixture = json.loads(subprocess.check_output([dotnet, str(root / 'tests/persistence/bin/Debug/net10.0/Learning.Persistence.dll'),
                                                         'plan-boundary-seed'], env=env, text=True).strip().splitlines()[-1])
            with socket.socket() as listener:
                listener.bind(('127.0.0.1', 0))
                port = listener.getsockname()[1]
            with open(Path(tmp) / 'api.log', 'w+') as log:
                child = subprocess.Popen([dotnet, str(root / 'src/server/bin/Debug/net10.0/Learning.Api.dll'), '--urls',
                                          f'http://127.0.0.1:{port}'], cwd=root / 'src/server', env=env, stdout=log, stderr=subprocess.STDOUT)
                deadline = time.monotonic() + 30
                while time.monotonic() < deadline:
                    assert child.poll() is None, 'isolated API exited'
                    try:
                        with urllib.request.urlopen(f'http://127.0.0.1:{port}/api/health', timeout=1) as response:
                            if response.status == 200:
                                break
                    except OSError:
                        time.sleep(.1)
                else:
                    raise AssertionError('isolated API unavailable')
                api_acceptance.BASE = f'http://127.0.0.1:{port}/api/v1'
                credentials = {'userName': env['PLAN_TEST_USERNAME'], 'password': env['PLAN_TEST_PASSWORD']}
                owner = Client()
                owner.request('/auth/login', credentials)
                owner.request('/me')
                # Create exactly three measured students; the seed student only supplies a published fixture.
                students = []
                days = [(date.fromisoformat(TODAY) - timedelta(days=20 - i)).isoformat() for i in range(20)]
                for index in range(3):
                    s = owner.request('/students', {'name': '性能验收学生' + str(index + 1), 'dailyMinutes': 30}, expected=201)
                    sid = s['id']
                    owner.request('/students/' + sid + '/content/' + fixture['releaseId'] + ':bind', {})
                    catalog = owner.request('/students/' + sid + '/catalog')
                    for day in days + [TODAY]:
                        owner.request('/students/' + sid + '/school-progress/' + day + '/' + catalog['lessons'][0]['id'], {}, method='PUT')
                    client = Client()
                    client.request('/auth/login', credentials)
                    client.request('/me')
                    students.append((sid, client))
                report['workload'].update(catalogQuestions=len(catalog['questions']), catalogKcs=len(catalog['kcs']))
                report['environment']['postgresVersion'] = subprocess.check_output(['psql', '-At', '-c', 'SHOW server_version'], env=env, text=True).strip()
                def timed(client, path, destination, body=None, expected=200):
                    start = time.perf_counter()
                    result = client.request(path, body, expected=expected)
                    destination.append((time.perf_counter() - start) * 1000)
                    return result
                review_questions = {sid:{} for sid, _ in students}
                def answer_flow(sid, client, tasks):
                    count = 0
                    for task in tasks:
                        q = next((q for q in catalog['questions'] if q['id'] == task.get('questionId')), None)
                        if q is None or q['type'] != 'Numeric' or q['policy'] != 'SingleKC':
                            continue
                        client.request('/tasks/' + task['id'] + ':transition', {'status': 'InProgress'})
                        session = client.request('/tasks/' + task['id'] + '/sessions', {})
                        kc = next(m['kcId'] for m in q['mappings'] if m['mode'] == 'WholeItem')
                        initial = kc not in review_questions[sid]
                        started = time.perf_counter()
                        result = timed(client, '/sessions/' + session['sessionId'] + '/attempts', answer_ms,
                                       {'clientSubmissionId': str(uuid.uuid4()), 'answer': q['answer'] + '1' if initial else q['answer']}, expected=201)
                        assert result['grading']['result'] == ('Incorrect' if initial else 'Correct')
                        aid = result['attempt']['id']
                        # Check this exact attempt in the active projection, rather than only a previous empty queue.
                        review_question = q['id'] if initial else review_questions[sid][kc]
                        deadline = time.monotonic() + 30
                        while time.monotonic() < deadline:
                            mastery = client.request('/students/' + sid + '/mastery')
                            if mastery['pending'] == 0:
                                detail = client.request('/students/' + sid + '/mastery/' + kc)
                                if any(e['attemptId'] == aid for e in detail['evidence']):
                                    reviews = client.request('/students/' + sid + '/reviews')
                                    assert any(r['generationId'] == mastery['generation'] and r['targetId'] == review_question
                                               and r['targetType'] == 'WrongQuestion' and r['stage'] == 'R1'
                                               for r in reviews), ('active review not rebuilt with the exact attempt evidence',
                                                                  {'generation':mastery['generation'],'target':review_question,'kc':kc,'reviews':reviews})
                                    projection_ms.append((time.perf_counter() - started) * 1000)
                                    review_questions[sid][kc] = review_question
                                    break
                            time.sleep(.05)
                        else:
                            raise AssertionError('exact attempt did not reach active evidence')
                        client.request('/tasks/' + task['id'] + ':transition', {'status': 'Completed'})
                        count += 1
                    assert count > 0, 'no rule-graded task measured'
                with concurrent.futures.ThreadPoolExecutor(max_workers=3) as pool:
                    for index, day in enumerate(days):
                        work = []
                        for sid, client in students:
                            client.request('/me')
                            revision = timed(client, '/students/' + sid + '/plans/' + day + ':generate', plan_ms, {})
                            assert revision['number'] == 1, 'cached plan accidentally measured as fresh generation'
                            view = client.request('/students/' + sid + '/plans/' + day)
                            client.request('/plans/' + revision['id'] + ':publish', {'previewHash': revision['inputHash'], 'confirmWarnings': True})
                            work.append((sid, client, view['tasks']))
                        futures = [pool.submit(answer_flow, *item) for item in work]
                        for future in futures:
                            future.result()
                        if (index + 1) % 5 == 0:
                            print(f'已测量 {index + 1}/20 轮，{len(answer_ms)} 次真实作答', flush=True)
                # Published current-day tasks enable the browser's actual start action after accumulated history.
                for sid, client in students:
                    client.request('/me')
                    revision = client.request('/students/' + sid + '/plans/' + TODAY + ':generate', {})
                    client.request('/plans/' + revision['id'] + ':publish', {'previewHash': revision['inputHash'], 'confirmWarnings': True})
                for _ in range(20):
                    for sid, client in students:
                        today = timed(client, '/students/' + sid + '/today', today_ms)
                        assert today['tasks'] and all(t['status']=='Ready' for t in today['tasks']), 'empty today page measured'
                report['metrics'] = {'freshPlan': distribution(plan_ms, 5000), 'answerPersistence': distribution(answer_ms, 1000),
                                     'answerToActiveEvidenceAndReview': distribution(projection_ms, 10000), 'todayApiOnly': distribution(today_ms, 2000)}
                report['workload']['persistedMeasuredAttempts'] = len(answer_ms)
                report['workload']['projectionObservation'] = '50ms polling plus exact attempt evidence and active-generation R1 review HTTP checks; upper-bound latency'
                report['workload']['retention'] = 'Actual answers are within 24h; repeated positive evidence remains suppressed and R1 does not advance'
                if os.environ.get('RUN_PERFORMANCE_BROWSER') == '1':
                    browser_file = Path(tmp) / 'browser.json'
                    browser_env = env | {'LEARNING_TEST_URL': f'http://127.0.0.1:{port}', 'PERFORMANCE_TEST_USER': credentials['userName'],
                                         'PERFORMANCE_TEST_PASSWORD': credentials['password'], 'PERFORMANCE_TEST_OUTPUT': str(browser_file)}
                    subprocess.run(['npm', 'run', 'test:e2e', '--', 'tests/performance.spec.ts', '--workers=1'], cwd=root / 'src/web', env=browser_env, check=True)
                    report['browser'] = json.loads(browser_file.read_text())
                report_path = root / 'docs/verification/local-performance.json'
                report_path.parent.mkdir(parents=True, exist_ok=True)
                report_path.write_text(json.dumps(report, ensure_ascii=False, indent=2) + '\n')
                print(json.dumps({k:{f:v for f,v in metric.items() if f!='rawMs'} for k,metric in report['metrics'].items()}, ensure_ascii=False, indent=2), flush=True)
                assert all(m['passed'] for m in report['metrics'].values()), 'local API p95 exceeded target; see saved evidence'
                print('PASS 本地API性能样本达到建议阈值；实际平板/Wi-Fi验收仍未完成', flush=True)
        finally:
            if child is not None and child.poll() is None:
                child.terminate()
                child.wait(timeout=10)
            subprocess.run(['dropdb', '--force', database], env=env, check=True)


if __name__ == '__main__':
    main()
