"""Disposable PostgreSQL and SDK configuration shared by CI persistence checks."""
import getpass,os,re,shutil,sys
from pathlib import Path

def dotnet(root):
    local=Path(root)/'.tools/dotnet/dotnet'
    if local.is_file():return str(local)
    installed=shutil.which('dotnet')
    if installed:return installed
    raise RuntimeError('A configured .NET SDK is required for persistence checks')

def environment(database):
    if not re.fullmatch(r'learning_fault_[A-Za-z0-9_]+',database):raise ValueError('Only disposable learning_fault_ databases allowed')
    env=os.environ.copy();host=env.get('PGHOST','127.0.0.1');port=int(env.get('PGPORT','55432' if sys.platform=='darwin' else '5432'));user=env.get('PGUSER',getpass.getuser())
    if not 1<=port<=65535:raise ValueError('Invalid PostgreSQL port')
    def quote(value):return '"'+value.replace('"','""')+'"'
    connection=f'Host={quote(host)};Port={port};Database={database};Username={quote(user)}'
    if env.get('PGPASSWORD'):connection+=';Password='+quote(env['PGPASSWORD'])
    env.update(PGHOST=host,PGPORT=str(port),PGUSER=user,PGDATABASE=database,PERSISTENCE_TEST_CONNECTION=connection)
    local=Path('/opt/homebrew/opt/postgresql@16/bin')
    if local.is_dir():env['PATH']=str(local)+os.pathsep+env['PATH']
    return env
