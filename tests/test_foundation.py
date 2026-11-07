import copy
import hashlib
import importlib.util
from pathlib import Path
import tempfile
import unittest
import zipfile
import uuid

ROOT=Path(__file__).resolve().parents[1]
def module(name,path):
    spec=importlib.util.spec_from_file_location(name,ROOT/path)
    obj=importlib.util.module_from_spec(spec); spec.loader.exec_module(obj); return obj
launcher=module('launcher','launcher/launcher.py')
compat=module('compat','protocol/compatibility.py')
package=module('package','scripts/package.py')

class FoundationTests(unittest.TestCase):
    def scratch(self):
        # Workspace-local fixtures avoid machine-specific temporary-directory ACLs.
        # Leave private fixtures in ignored .local; packaging never visits them.
        path=(ROOT/'.local/tests'/uuid.uuid4().hex).resolve()
        assert path.is_relative_to(ROOT/'.local/tests')
        path.mkdir(parents=True)
        return path
    def test_rom_hash_and_zip_names(self):
        data=b'\0'*launcher.ROM_SIZE
        digest=hashlib.sha1(data).hexdigest()
        tmp=self.scratch()
        path=tmp/'backup.zip'
        with zipfile.ZipFile(path,'w',zipfile.ZIP_DEFLATED) as z: z.writestr('../../escape.z64',data)
        self.assertEqual(launcher.read_rom(path,expected_sha1=digest),data)
        self.assertFalse((tmp/'escape.z64').exists())
        with self.assertRaises(ValueError): launcher.read_rom(path)
        with zipfile.ZipFile(path,'a',zipfile.ZIP_DEFLATED) as z: z.writestr('second.z64',data)
        with self.assertRaises(ValueError): launcher.read_rom(path,expected_sha1=digest)
    def test_wrong_size(self):
        p=self.scratch()/'bad.z64';p.write_bytes(b'wrong')
        with self.assertRaises(ValueError): launcher.read_rom(p)
    def test_simulation_identity(self):
        a={'protocol':1,'engine_commit':'a','engine_patch_sha256':'b','rom_sha1':'c','simulation_hz':60,'rules':{'mode':'deathmatch'},'mods':[]}
        b=copy.deepcopy(a);b['mods']=[{'id':'paint','sha256':'d','kind':'visual'}]
        self.assertEqual(compat.simulation_fingerprint(a),compat.simulation_fingerprint(b))
        b['mods'][0]['kind']='simulation'
        self.assertNotEqual(compat.simulation_fingerprint(a),compat.simulation_fingerprint(b))
        b=copy.deepcopy(a);b['simulation_hz']=144
        self.assertNotEqual(compat.simulation_fingerprint(a),compat.simulation_fingerprint(b))
    def test_package_exclusions(self):
        for path in ['Data/baserom.z64','launcher/secret.exe','assets/model.c','.local/user.json','docs/screenshot.png','scripts/.env','source/game.c','docs/credentials.json']:
            self.assertFalse(package.approved(path),path)
        self.assertTrue(package.approved('launcher/launcher.py'))

if __name__=='__main__': unittest.main()
