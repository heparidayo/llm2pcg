import {readFileSync} from 'node:fs';
import {parseStrictJson} from '../Shared/strict-json.mjs';
import {proposeV4Migration} from '../Web/src/migrate-v3-to-v4.mjs';
if(!process.argv[2])throw Error('Usage: node Scripts/Propose-V4Migration.mjs saved-v3-request.json');
console.log(JSON.stringify(proposeV4Migration(parseStrictJson(readFileSync(process.argv[2],'utf8'))),null,2));
