const fs=require('node:fs'),vm=require('node:vm'),assert=require('node:assert/strict');
const script=fs.readFileSync(process.argv[2],'utf8');
class Track{
  constructor(label){this.label=label;this.readyState='live';this.events={};}
  addEventListener(name,cb){this.events[name]=cb;}
  getSettings(){return{};}
  stop(){this.readyState='ended';}
}
class Stream{
  constructor(label){this.audio=[new Track(label)];}
  getAudioTracks(){return this.audio;}getVideoTracks(){return[];}getTracks(){return this.audio;}
  addTrack(t){this.audio.push(t);}
}
async function scenario({ready=false,enabled=false,failedCable=false,defaultCable=false,blockedGraph=false}={}){
  let now=1000,physical,cable,connection,ctx;
  const listeners=[],reports=[],calls=[],intervals=[];
  const media={enumerateDevices:async()=>[
    {kind:'audioinput',label:'Microphone array',deviceId:'physical'},
    {kind:'audioinput',label:'CABLE Output (VB-Audio Virtual Cable)',deviceId:'cable'}],
    getUserMedia:async c=>{
      calls.push(c);
      if(c.audio?.deviceId?.exact==='cable'){
        if(failedCable)throw Error('unavailable');return cable=new Stream('CABLE Output (VB-Audio Virtual Cable)');
      }
      if(defaultCable && !c.audio?.deviceId)return new Stream('CABLE Output (VB-Audio Virtual Cable)');
      return physical=new Stream('Microphone array');
    }};
  class Socket{addEventListener(){}}
  const window={WebSocket:Socket,addEventListener:(n,cb)=>{if(n==='message')listeners.push(cb);},postMessage:r=>reports.push(r),
    AudioContext:class{
      constructor(){ctx=this;this.state='suspended';}
      async resume(){this.state=blockedGraph?'suspended':'running';}
      createMediaStreamDestination(){return{stream:new Stream('Graph microphone')};}
      createMediaStreamSource(s){return{connect(){connection=s;},disconnect(){}};}
      close(){this.closed=true;return Promise.resolve();}
    }};
  const context={window,navigator:{mediaDevices:media},document:{querySelectorAll:()=>[]},location:{origin:'https://chatgpt.com'},Date:{now:()=>now},
    setInterval:cb=>intervals.push(cb),performance:{now:()=>now},Blob,ArrayBuffer,TextDecoder};
  vm.runInNewContext(script,context);
  const health=r=>listeners.forEach(cb=>cb({source:window,origin:'https://chatgpt.com',data:{source:'voice-bridge-aec',ready:r,enabled}}));
  health(ready);
  const output=await media.getUserMedia({audio:true});
  const tick=async()=>{await new Promise(r=>setImmediate(r));await new Promise(r=>setImmediate(r));};
  return{output,physical,cable,ctx,calls,reports,health,tick,get connection(){return connection;},expire:()=>{now+=7000;intervals.forEach(cb=>cb());}};
}
(async()=>{
  let s=await scenario();assert.equal(s.output,s.physical);assert.equal(s.calls.length,1);
  s=await scenario({ready:true});assert.equal(s.connection,s.cable);
  const stable=s.output.getAudioTracks()[0];s.health(false);await s.tick();
  assert.equal(s.connection,s.physical);assert.equal(stable.readyState,'live');assert.equal(s.cable.getAudioTracks()[0].readyState,'ended');
  s.health(true);await s.tick();assert.equal(s.connection.getAudioTracks()[0].label,'CABLE Output (VB-Audio Virtual Cable)');
  s.expire();await s.tick();assert.equal(s.connection,s.physical);
  stable.stop();assert.equal(s.physical.getAudioTracks()[0].readyState,'ended');assert.equal(s.ctx.closed,true);
  s=await scenario({ready:true,failedCable:true});assert.equal(s.connection,s.physical);assert.equal(s.output.getAudioTracks()[0].readyState,'live');
  s.output.getAudioTracks()[0].stop();
  s=await scenario({ready:true,blockedGraph:true});assert.equal(s.output,s.physical);assert.equal(s.ctx.closed,true);
  s=await scenario({defaultCable:true});assert.equal(s.output,s.physical);assert.equal(s.physical.getAudioTracks()[0].readyState,'live');
  s=await scenario({enabled:true});assert.equal(s.connection,s.physical);
  s.health(true);await s.tick();assert.equal(s.connection.getAudioTracks()[0].label,'CABLE Output (VB-Audio Virtual Cable)');s.output.getAudioTracks()[0].stop();
  console.log('PASS: live physical fallback, stable track after helper loss/restart/expiry, failed virtual input, suspended graph, cleanup, stale virtual default');
})().catch(e=>{console.error(e);process.exitCode=1;});
