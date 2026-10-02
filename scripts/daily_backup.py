#!/usr/bin/env python3
"""Private local backup job. Archive verification is distinct from a restore exercise."""
import argparse
import fcntl
import hashlib
import json
import os
import re
import shutil
import signal
import stat
import subprocess
import tempfile
import time
import uuid
from datetime import datetime, timedelta, timezone
from pathlib import Path

FORMAT = 'learning-backup/1'
NAME = re.compile(r'^learning-\d{8}T\d{6}Z-[0-9a-f]{12}\.pgdump\.gpg$')


class BackupError(Exception):
    pass


def utc():
    return datetime.now(timezone.utc)


def stamp(value):
    return value.isoformat()


def digest(path):
    with Path(path).open('rb') as source:
        return hashlib.file_digest(source, 'sha256').hexdigest()


def date(value):
    result = datetime.fromisoformat(value)
    if result.tzinfo is None:
        raise BackupError('INVALID_TIMESTAMP')
    return result.astimezone(timezone.utc)


def private_file(path):
    path = Path(path)
    info = path.lstat()
    if not stat.S_ISREG(info.st_mode) or info.st_uid != os.getuid() or info.st_mode & 0o077:
        raise BackupError('PRIVATE_FILE_REQUIRED')
    return path


def private_dir(path):
    path = Path(path)
    path.mkdir(parents=True, exist_ok=True, mode=0o700)
    info = path.lstat()
    if not stat.S_ISDIR(info.st_mode) or info.st_uid != os.getuid() or info.st_mode & 0o077:
        raise BackupError('PRIVATE_DIRECTORY_REQUIRED')
    return path


def write_json(path, value):
    path = Path(path)
    private_dir(path.parent)
    with tempfile.NamedTemporaryFile(dir=path.parent, prefix='.backup-status-', delete=False) as out:
        pending = Path(out.name)
        try:
            out.write((json.dumps(value, ensure_ascii=False, indent=2) + '\n').encode())
            out.flush()
            os.fsync(out.fileno())
            os.replace(pending, path)
        finally:
            pending.unlink(missing_ok=True)
    fd = os.open(path.parent, os.O_RDONLY)
    try:
        os.fsync(fd)
    finally:
        os.close(fd)


def run_command(args, env, timeout, data=None):
    process = subprocess.Popen(args, env=env, stdin=subprocess.PIPE if data is not None else subprocess.DEVNULL,
                               stdout=subprocess.PIPE, stderr=subprocess.PIPE, start_new_session=True)
    try:
        output, _ = process.communicate(input=data, timeout=timeout)
    except BaseException:
        if process.poll() is None:
            os.killpg(process.pid, signal.SIGKILL)
        process.communicate()
        raise
    if process.returncode:
        # Never persist command/environment/error text that could include private connection details.
        raise BackupError('TOOL_FAILED_' + Path(args[0]).name.upper().replace('-', '_'))
    return output


def load_config(path):
    config = json.loads(private_file(path).read_text())
    required = ['backupDir', 'stateFile', 'passphraseFile', 'dataDir', 'deletionLedger', 'familyDeletionLedger', 'postgres']
    if any(k not in config for k in required):
        raise BackupError('CONFIG_INCOMPLETE')
    for key in required[:-1]:
        if not Path(config[key]).is_absolute():
            raise BackupError('ABSOLUTE_PATH_REQUIRED')
    pg = config['postgres']
    if set(pg) != {'host', 'port', 'user', 'database'} or pg['host'] not in {'127.0.0.1', 'localhost', '::1'}:
        raise BackupError('LOCAL_DATABASE_REQUIRED')
    config['intervalHours'] = config.get('intervalHours', 23)
    config['retentionDays'] = config.get('retentionDays', 30)
    config['timeoutSeconds'] = config.get('timeoutSeconds', 900)
    if not 1 <= config['intervalHours'] <= 23 or config['retentionDays'] != 30 or not 10 <= config['timeoutSeconds'] <= 1800:
        raise BackupError('INVALID_POLICY')
    return config


def prune(directory, now, days=30):
    """Only this job's recognizable, fully described expired archive pairs are eligible."""
    removed = []
    for archive in directory.iterdir():
        if not NAME.fullmatch(archive.name) or archive.is_symlink() or not archive.is_file():
            continue
        manifest = archive.with_name(archive.name + '.json')
        try:
            value = json.loads(private_file(manifest).read_text())
            if value.get('format') != FORMAT or value.get('archive') != archive.name or not value.get('archiveVerified'):
                continue
            started = date(value['snapshotStartedAt'])
            # Creation time comes from the snapshot manifest and filename, not an arbitrary mtime.
            if started.strftime('%Y%m%dT%H%M%SZ') != archive.name.split('-')[1]:
                continue
            if started >= now - timedelta(days=days) or started > now:
                continue
            if digest(archive) != value['sha256']:
                continue
        except (OSError, ValueError, KeyError, BackupError):
            continue
        archive.unlink()
        manifest.unlink()
        removed.append(archive.name)
    return removed


def execute(config, force=False):
    state_file = Path(config['stateFile'])
    private_dir(state_file.parent)
    lock_fd = os.open(state_file.with_suffix('.lock'), os.O_CREAT | os.O_RDWR | os.O_NOFOLLOW, 0o600)
    try:
        try:
            fcntl.flock(lock_fd, fcntl.LOCK_EX | fcntl.LOCK_NB)
        except BlockingIOError:
            return {'outcome': 'AlreadyRunning'}
        previous = {}
        if state_file.exists():
            previous = json.loads(private_file(state_file).read_text())
        now = utc()
        last = previous.get('lastSuccess')
        config_hash = hashlib.sha256(json.dumps(config, sort_keys=True).encode()).hexdigest()
        last_archive = Path(config['backupDir']) / last['archive'] if last and NAME.fullmatch(last.get('archive', '')) else None
        current_archive = last_archive and not last_archive.is_symlink() and last_archive.is_file() and last_archive.stat().st_size == last.get('bytes')
        if not force and last and current_archive and previous.get('configHash') == config_hash and date(last['snapshotStartedAt']) > now - timedelta(hours=config['intervalHours']) and previous.get('status') == 'Succeeded':
            return {'outcome': 'NotDue'}
        # Failed jobs wait at least five minutes; --force is for an explicit operator recovery.
        if not force and previous.get('status') == 'Failed' and previous.get('configHash') == config_hash and date(previous.get('failedAt',previous['lastAttemptAt'])) > now - timedelta(minutes=5):
            return {'outcome': 'Backoff'}
        state = {'format': FORMAT, 'status': 'Running', 'lastAttemptAt': stamp(now), 'lastSuccess': last,
                 'intervalHours': config['intervalHours'], 'retentionDays': config['retentionDays'],
                 'archiveVerified': False, 'restoreVerified': False, 'independentDisk': False, 'configHash': config_hash}
        write_json(state_file, state)
        try:
            directory = private_dir(config['backupDir'])
            data_dir = Path(config['dataDir'])
            if not data_dir.is_dir():
                raise BackupError('DATA_DIRECTORY_UNAVAILABLE')
            state['separateFilesystem'] = directory.stat().st_dev != data_dir.stat().st_dev
            # Distinct APFS volumes may still share one physical disk. Do not infer physical independence.
            state['independentDisk'] = False
            if config.get('requireIndependentDisk', False) and not state['independentDisk']:
                raise BackupError('INDEPENDENT_DISK_REQUIRED')
            # The authoritative live deletion ledgers stay outside the DB dump and must remain available.
            for key in ['deletionLedger', 'familyDeletionLedger']:
                private_file(config[key])
            passphrase = private_file(config['passphraseFile']).read_bytes().strip()
            if len(passphrase) < 32 or b'\n' in passphrase or b'\r' in passphrase:
                raise BackupError('INVALID_PASSPHRASE_FILE')
            tools = {name: shutil.which(name) for name in ['pg_dump', 'pg_restore', 'psql', 'gpg', 'gpgconf']}
            if not all(tools.values()):
                raise BackupError('BACKUP_TOOLS_UNAVAILABLE')
            env = os.environ.copy()
            env.update({{'host': 'PGHOST', 'port': 'PGPORT', 'user': 'PGUSER', 'database': 'PGDATABASE'}[k]: str(v)
                        for k, v in config['postgres'].items()})
            timeout = config['timeoutSeconds']
            deadline = time.monotonic() + timeout
            def invoke(args, data=None):
                remaining = deadline - time.monotonic()
                if remaining <= 0:
                    raise BackupError('BACKUP_TIMEOUT')
                return run_command(args, env, remaining, data)
            archive = directory / f'learning-{now:%Y%m%dT%H%M%SZ}-{uuid.uuid4().hex[:12]}.pgdump.gpg'
            staged = archive.with_name('.' + archive.name + '.partial')
            try:
                with tempfile.TemporaryDirectory(prefix='learning-backup-', dir='/private/tmp') as work:
                    work = Path(work)
                    gpg_home = work / 'gpg'
                    gpg_home.mkdir(mode=0o700)
                    env['GNUPGHOME'] = str(gpg_home)
                    plain, checked = work / 'database.pgdump', work / 'checked.pgdump'
                    try:
                        tables = invoke([tools['psql'], '-At', '-v', 'ON_ERROR_STOP=1', '-c',
                                         "SELECT tablename FROM pg_tables WHERE schemaname='public' ORDER BY tablename"]).decode().splitlines()
                        if not {'Families', 'Students', 'Attempts', 'Evidence', 'Reviews', 'Releases', 'PrivateFile'} <= set(tables):
                            raise BackupError('LEARNING_SCHEMA_REQUIRED')
                        invoke([tools['pg_dump'], '--format=custom', '--no-owner', '--file=' + str(plain)])
                        gpg = [tools['gpg'], '--batch', '--yes', '--pinentry-mode', 'loopback', '--no-symkey-cache', '--passphrase-fd', '0']
                        invoke(gpg + ['--symmetric', '--cipher-algo', 'AES256', '--output', str(staged), str(plain)], passphrase)
                        invoke(gpg + ['--decrypt', '--output', str(checked), str(staged)], passphrase)
                        if digest(plain) != digest(checked):
                            raise BackupError('DECRYPTED_ARCHIVE_MISMATCH')
                        listing = invoke([tools['pg_restore'], '--list', str(checked)]).decode()
                        listed = {line.split(' TABLE public ', 1)[1].split()[0] for line in listing.splitlines() if ' TABLE public ' in line}
                        if set(tables) != listed:
                            raise BackupError('ARCHIVE_TABLES_MISMATCH')
                    finally:
                        run_command([tools['gpgconf'], '--kill', 'gpg-agent'], env, 10)
                os.chmod(staged, 0o600)
                with staged.open('rb') as handle:
                    os.fsync(handle.fileno())
                os.replace(staged, archive)
                manifest = {'format': FORMAT, 'archive': archive.name, 'snapshotStartedAt': stamp(now),
                            'completedAt': stamp(utc()), 'archiveVerified': True, 'restoreVerified': False,
                            'sha256': digest(archive), 'bytes': archive.stat().st_size,
                            'tableCount': len(tables), 'cipher': 'AES256', 'independentDisk': state['independentDisk'],
                            'separateFilesystem': state['separateFilesystem']}
                write_json(archive.with_name(archive.name + '.json'), manifest)
                removed = prune(directory, utc(), config['retentionDays'])
                state.update(status='Succeeded', archiveVerified=True, lastSuccess=manifest, errorCode=None,
                             nextDueAt=stamp(now + timedelta(hours=config['intervalHours'])), removedArchives=len(removed))
                write_json(state_file, state)
                return {'outcome': 'Succeeded', 'archive': str(archive), 'archiveVerified': True,
                        'restoreVerified': False, 'independentDisk': state['independentDisk']}
            finally:
                staged.unlink(missing_ok=True)
        except Exception as error:
            state.update(status='Failed', failedAt=stamp(utc()), errorCode=str(error) if isinstance(error, BackupError) else 'BACKUP_TIMEOUT' if isinstance(error, subprocess.TimeoutExpired) else 'BACKUP_EXECUTION_FAILED')
            write_json(state_file, state)
            raise BackupError(state['errorCode']) from None
    finally:
        os.close(lock_fd)


def main():
    os.umask(0o077)
    parser = argparse.ArgumentParser()
    parser.add_argument('--config', required=True)
    parser.add_argument('--force', action='store_true')
    parser.add_argument('--watch', action='store_true', help='Check due work every minute while this local process runs')
    args = parser.parse_args()
    try:
        if args.watch and args.force:
            raise BackupError('WATCH_CANNOT_FORCE')
        while True:
            try:
                config=load_config(args.config)
                heartbeat=Path(config['stateFile']).with_suffix('.heartbeat.json')
                write_json(heartbeat, {'format':FORMAT,'pid':os.getpid(),'observedAt':stamp(utc()),'status':'Checking'})
                result=execute(config,args.force)
                write_json(heartbeat, {'format':FORMAT,'pid':os.getpid(),'observedAt':stamp(utc()),'status':'Waiting'})
                if result['outcome'] not in {'NotDue','Backoff'}:
                    print(json.dumps(result,ensure_ascii=False),flush=True)
            except (BackupError,OSError,ValueError) as error:
                print(json.dumps({'outcome':'Failed','errorCode':str(error) if isinstance(error,BackupError) else 'CONFIG_UNAVAILABLE'}),flush=True)
                if not args.watch:
                    raise SystemExit(1)
            if not args.watch:
                break
            time.sleep(60)
    except (BackupError, OSError, ValueError) as error:
        print(json.dumps({'outcome': 'Failed', 'errorCode': str(error) if isinstance(error, BackupError) else 'CONFIG_UNAVAILABLE'}))
        raise SystemExit(1)


if __name__ == '__main__':
    main()
