"""Execute an actual historical review/1 assembly, then upgrade its untouched database."""
import io,subprocess,tarfile,tempfile,uuid
from pathlib import Path
from persistence_environment import environment,dotnet

def main():
    root=Path(__file__).resolve().parents[1]
    database='learning_fault_'+uuid.uuid4().hex[:12]
    env=environment(database)
    with tempfile.TemporaryDirectory(prefix='review-rule-upgrade-') as directory:
        work=Path(directory).resolve()
        archive=subprocess.check_output(['git','archive','1097143','src/server','Directory.Build.props'],cwd=root)
        with tarfile.open(fileobj=io.BytesIO(archive)) as source:source.extractall(work,filter='data')
        runner=work/'tests/persistence';runner.mkdir(parents=True)
        (runner/'ReviewRuleUpgradeCases.cs').write_bytes((root/'tests/persistence/ReviewRuleUpgradeCases.cs').read_bytes())
        (runner/'Learning.Persistence.csproj').write_bytes((root/'tests/persistence/Learning.Persistence.csproj').read_bytes())
        (runner/'Program.cs').write_text('using Learning; using Microsoft.EntityFrameworkCore; var connection=Environment.GetEnvironmentVariable("PERSISTENCE_TEST_CONNECTION")!; if(!connection.Contains("Database=learning_fault_"))throw new Exception("Disposable database required"); await using var db=new Database(new DbContextOptionsBuilder<Database>().UseNpgsql(connection).Options); await ReviewRuleUpgradeCases.Run(db,args[0]);')
        env['REVIEW_UPGRADE_PROOF']=str(work/'proof.json')
        subprocess.run([dotnet(root),'build',str(runner),'--ignore-failed-sources'],env=env,check=True)
        subprocess.run(['createdb',database],env=env,check=True)
        try:
            subprocess.run([dotnet(root),str(runner/'bin/Debug/net10.0/Learning.Persistence.dll'),'review-rule-seed'],env=env,check=True)
            subprocess.run([dotnet(root),str(root/'tests/persistence/bin/Debug/net10.0/Learning.Persistence.dll'),'review-rule-upgrade'],env=env,check=True)
        finally:subprocess.run(['dropdb','--force',database],env=env,check=True)

if __name__=='__main__':main()
