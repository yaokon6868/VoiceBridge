const fs=require('node:fs'),vm=require('node:vm'),assert=require('node:assert/strict');
const state={}, tabs=new Map([[1,{mutedInfo:{muted:false}}],[2,{mutedInfo:{muted:true,reason:'user'}}]]);
let receive,alarm;
const chrome={runtime:{id:'bridge',onMessage:{addListener:f=>receive=f}},storage:{session:{
  async get(k){return k===null?{...state}:{[k]:state[k]};},async set(v){Object.assign(state,v);},async remove(k){delete state[k];}
}},tabs:{async get(id){return tabs.get(id);},async update(id,v){tabs.get(id).mutedInfo={muted:v.muted,extensionId:'bridge'};},onUpdated:{addListener(){}},onRemoved:{addListener(){}}},alarms:{create(){},onAlarm:{addListener:f=>alarm=f}}};
let ready=true;
vm.runInNewContext(fs.readFileSync(process.argv[2],'utf8'),{chrome,Map,Promise,Date,Object,AbortSignal,fetch:async()=>({ok:true,json:async()=>({ttsReady:ready,token:'test-session'})})});
const send=(id,enabled)=>new Promise(resolve=>receive({kind:'mute',enabled},{tab:{id},url:'https://chatgpt.com/'},resolve));
(async()=>{
  await send(1,true);assert.equal(tabs.get(1).mutedInfo.muted,true);
  await send(1,false);assert.equal(tabs.get(1).mutedInfo.muted,false);
  await send(2,true);await send(2,false);assert.equal(tabs.get(2).mutedInfo.muted,true);
  await send(1,true);state['mute-1'].expires=0;await alarm({name:'voice-mute-watchdog'});
  assert.equal(tabs.get(1).mutedInfo.muted,false);
  ready=false;await send(1,true);assert.equal(tabs.get(1).mutedInfo.muted,false);
  console.log('PASS: owned mute/restoration, existing user mute preserved, stale connection recovery');
})().catch(error=>{console.error(error);process.exitCode=1;});
