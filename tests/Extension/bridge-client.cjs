const fs=require('node:fs'),vm=require('node:vm'),assert=require('node:assert/strict');
let receive,current='one',pairs=0,posts=0,legacy=false,unknown=false,deny=false;
const requests=[];
const listener={addListener(){}};
const chrome={runtime:{id:'aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa',onMessage:{addListener:f=>receive=f}},
  tabs:{onUpdated:listener,onRemoved:listener},alarms:{create(){},onAlarm:listener}};
const response=(status,data)=>({status,ok:status>=200&&status<300,json:async()=>data});
const fetch=async(url,opts={})=>{
  requests.push({url,opts});
  if(url.endsWith('/session')){pairs++;return deny?response(403,{}):legacy?response(404,{}):response(200,{token:current});}
  if(legacy)return response(200,{service:unknown?'Unknown':'VoiceBridge Next',version:'0.3.0-preview',instanceId:'legacy'});
  if(opts.headers?.Authorization!==`Bearer ${current}`)return response(401,{});
  if(url.endsWith('/event'))posts++;
  return response(200,{ttsReady:true,instanceId:'test'});
};
function load(){vm.runInNewContext(fs.readFileSync(process.argv[2],'utf8'),{chrome,fetch,AbortSignal,Map,Promise,Date,Object,Error});}
const popup=kind=>new Promise(resolve=>receive({kind},{id:chrome.runtime.id},resolve));
const text=()=>new Promise(resolve=>receive({kind:'event',event:{type:'text',sessionId:'one',text:'Hello.'}},{tab:{id:7},url:'https://chatgpt.com/'},resolve));
(async()=>{
  load();assert.equal((await popup('health')).ok,true);assert.equal(pairs,1);
  assert.equal((await text()).ok,true);assert.equal(posts,1);
  current='two';assert.equal((await text()).ok,true);assert.equal(posts,2);assert.equal(pairs,2);
  assert.equal((await popup('diagnostics')).ok,true);
  assert.equal(requests.some(r=>JSON.stringify(r.opts.body||'').includes('Bearer')),false);
  console.log('PASS: extension pairs, authenticates, renews after restart, and submits each event once');
  deny=true;load();assert.equal((await popup('health')).ok,false);
  console.log('PASS: denied bootstrap cannot downgrade authentication');
  deny=false;legacy=true;load();assert.equal((await popup('health')).ok,true);
  unknown=true;load();assert.equal((await popup('health')).ok,false);
  console.log('PASS: migration accepts only identified already-running preview');
})().catch(e=>{console.error(e);process.exitCode=1;});
