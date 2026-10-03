const fs = require('node:fs');
const vm = require('node:vm');
const assert = require('node:assert/strict');
class Socket {
  constructor(){ this.events={}; }
  addEventListener(name,cb){this.events[name]=cb;}
}
class Peer extends Socket {
  createDataChannel(){return new Socket();}
}
const sent=[];
const window={WebSocket:Socket,RTCPeerConnection:Peer,postMessage:m=>sent.push(m),addEventListener(){}};
const context={window,document:{querySelectorAll:()=>[]},performance:{now:()=>1},location:{origin:'https://chatgpt.com'},setInterval(){},Map,Set,Proxy,JSON,Blob,ArrayBuffer,TextDecoder};
vm.runInNewContext(fs.readFileSync(process.argv[2],'utf8'),context);
const ws=new window.WebSocket('wss://example.test');
ws.events.message({data:JSON.stringify({type:'response.audio_transcript.delta',delta:'你好',response_id:'r1'})});
assert.equal(sent.at(-1).text,'你好');
const pc=new window.RTCPeerConnection();
const channel=pc.createDataChannel('oai-events');
channel.events.message({data:JSON.stringify({type:'response.output_audio_transcript.delta',delta:'连续朗读',response_id:'r2'})});
assert.equal(sent.at(-1).text,'连续朗读');
const remote=new Socket(); pc.events.datachannel({channel:remote});
remote.events.message({data:JSON.stringify({type:'response.done'})});
assert.equal(sent.at(-1).kind,'network-stop');
ws.events.message({data:JSON.stringify([{type:'conversation-update',payload:{update_content:{message:{id:'nested',author:{role:'assistant'},content:{content_type:'text',parts:['完整回复']},status:'finished_successfully'}}}}])});
assert.equal(sent.at(-1).text,'完整回复');
assert.equal(sent.at(-1).complete,true);
const count=sent.length;
ws.events.message({data:JSON.stringify({author:{role:'user'},content:{content_type:'text',parts:['不应朗读']}})});
assert.equal(sent.length,count);
console.log('PASS: hook initializes without cyclic prototype; WebSocket + local/remote WebRTC channels capture text and completion');
