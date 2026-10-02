"""Family ownership, normalized permissions, private ZIP and deletion on isolated families."""
import base64,copy,io,json,urllib.request,uuid,zipfile
from pathlib import Path
from api_acceptance import Client,BASE

def main():
    owner=Client();other=Client();password='family-test-'+uuid.uuid4().hex
    for c in [owner,other]:c.request('/auth/register',{'userName':'family-'+uuid.uuid4().hex[:12],'password':password},expected=201);c.request('/me')
    identity=owner.request('/me');fid=identity['family']['id'];assert identity['family']['ownerAccountId']==identity['actor']['accountId']
    sid=owner.request('/students',{'name':'家庭权限验收'},expected=201)['id']
    draft=owner.request('/content/fixture',{});owner.request('/content/drafts/'+draft['id']+':review',{});p=owner.request('/content/drafts/'+draft['id']+'/preview');release=owner.request('/content/drafts/'+draft['id']+':publish',{'previewHash':p['hash']})
    png='iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aS1cAAAAASUVORK5CYII='
    file=owner.request('/files',{'name':'../../private.png','mimeType':'image/png','base64':png},expected=201)
    owner.request('/students/'+sid+'/paper-wrongs',{'stem':'私有错题','answer':'999','fileId':file['id']},expected=201)
    clients={};members={}
    for role in ['Parent','ContentEditor','Publisher']:
        name='member-'+uuid.uuid4().hex[:12];m=owner.request('/family/members',{'userName':name,'password':password,'roles':[role]},expected=201);members[role]=m
        c=Client();c.request('/auth/login',{'userName':name,'password':password});c.request('/me');clients[role]=c
        c.request('/family/members',expected=403);c.request('/family/delete-preview',expected=403);c.request('/family/export',expected=403)
    parent=clients['Parent'];assert len(parent.request('/students'))==1 and parent.request('/content')['drafts']==[]
    parent.request('/content/fixture',{},expected=403)
    for role in ['ContentEditor','Publisher']:
        c=clients[role];assert c.request('/students')==[]
        c.request('/students/'+sid+'/catalog',expected=403);c.request('/students/'+sid+'/paper-wrongs',expected=403);c.request('/files/'+file['id'],expected=404)
        d=c.request('/content/drafts',{'title':'成员修订审核','catalog':json.loads(draft['payload'])});c.request('/content/drafts/'+d['id']+':review',{});p=c.request('/content/drafts/'+d['id']+'/preview',expected=403 if role=='ContentEditor' else 200)
        c.request('/content/drafts/'+d['id']+':publish',{'previewHash':p.get('hash','blocked')},expected=403 if role=='ContentEditor' else 200)
    print('PASS 负责人独享全家管理；家长、编辑、发布权限独立，编辑无法访问学生记录及私有错题图')
    owner.request('/me')
    ownmember=next(m for m in owner.request('/family/members') if m['accountId']==identity['actor']['accountId'])
    owner.request('/family/members/'+ownmember['id']+'/roles',{'roles':None,'reason':'缺少明确权限'},method='PUT',expected=422)
    owner.request('/family/members/'+ownmember['id']+'/roles',{'roles':['ContentEditor'],'reason':'测试不得移除负责人家长权限'},method='PUT',expected=422)
    # A separate login avoids replacing the owner's cookie with the child session.
    oldparent=Client()
    for cookie in parent.jar:oldparent.jar.set_cookie(copy.copy(cookie))
    oldparent.request('/me');cached_key=str(uuid.uuid4());cached_body={'name':'权限缓存验收'};oldparent.request('/students',cached_body,key=cached_key,expected=201)
    parent.request('/me');parent.request('/students/'+sid+'/child-sessions',{});parent.request('/family/export',expected=403)
    owner.request('/me');owner.request('/family/members/'+members['ContentEditor']['id']+'/roles',{'roles':[],'reason':'撤销内容权限'},method='PUT')
    clients['ContentEditor'].request('/me',expected=401);parent.request('/me',expected=401)
    owner.request('/family/members/'+members['Parent']['id']+'/roles',{'roles':['ContentEditor'],'reason':'调整为仅内容编辑'},method='PUT');oldparent.request('/me',expected=401)
    renewed=Client();renewed.request('/auth/login',{'userName':members['Parent']['userName'],'password':password});renewed.request('/me');renewed.request('/students',cached_body,key=cached_key,expected=403)
    print('PASS 成员权限更新撤销旧成员及孩子会话；负责人必须保留家长权限')
    with owner.http.open(urllib.request.Request(BASE+'/family/export')) as r:
        assert r.status==200 and r.headers['Content-Type']=='application/zip';payload=r.read()
    with zipfile.ZipFile(io.BytesIO(payload)) as z:
        manifest=json.loads(z.read('manifest.json'));assert manifest['format']=='learning-family-export/1' and manifest['family']['id']==fid
        assert any(s['id']==sid for s in manifest['data']['Student']) and len(manifest['members'])==4
        assert 'Account' not in manifest['data'] and 'AuthSession' not in manifest['data'] and 'CommandRecord' not in manifest['data']
        item=manifest['files'][0];assert z.read(item['archivePath'])==base64.b64decode(png) and '..' not in item['archivePath'].split('/')
        encoded=json.dumps(manifest).lower();assert not any(k in encoded for k in ['passwordhash','tokenhash','cookiecipher'])
        for rows in manifest['data'].values():assert all(row['familyId']==fid for row in rows)
    print('PASS ZIP 包含本家庭业务历史与原始附件，安全路径不泄露登录凭据或其他家庭数据')
    preview=owner.request('/family/delete-preview')
    owner.request('/family:delete',{'previewHash':preview['previewHash'],'password':'invalid','confirm':'永久删除家庭'},expected=422)
    owner.request('/students',{'name':'范围变更'},expected=201)
    owner.request('/family:delete',{'previewHash':preview['previewHash'],'password':password,'confirm':'永久删除家庭'},expected=412)
    preview=owner.request('/family/delete-preview');receipt=owner.request('/family:delete',{'previewHash':preview['previewHash'],'password':password,'confirm':'永久删除家庭'})
    assert receipt['deleted'] and receipt['familyId']==fid and receipt['receiptId']
    owner.request('/me',expected=401);clients['Publisher'].request('/me',expected=401);other.request('/me')
    ledger=Path(__file__).resolve().parent.parent/'.local/deleted-families.txt';assert fid+','+receipt['receiptId'] in ledger.read_text()
    print('PASS 删除要求密码及有效范围预览；级联撤销全家会话，独立回执清单保留且其他家庭仍可用')
if __name__=='__main__':main()
