"""Proposed compatibility identity, not a network protocol implementation."""
import hashlib
import json

def simulation_fingerprint(manifest):
    required=('protocol','engine_commit','engine_patch_sha256','rom_sha1','simulation_hz','rules')
    payload={key:manifest[key] for key in required}
    mods=[]
    for mod in manifest.get('mods',[]):
        # Missing classification is conservative: it affects the simulation.
        if mod.get('kind') != 'visual':
            mods.append({'id':mod['id'],'sha256':mod['sha256']})
    payload['simulation_mods']=sorted(mods,key=lambda item:(item['id'],item['sha256']))
    encoded=json.dumps(payload,sort_keys=True,separators=(',',':'),ensure_ascii=True).encode()
    return hashlib.sha256(encoded).hexdigest()
