const fs=require('node:fs'),vm=require('node:vm'),assert=require('node:assert/strict');
let elements=[],voice=true,observer,now=100000;
const events=[],timers=new Map();let nextTimer=1;
const element=text=>({innerText:text,matches:()=>true,closest:()=>null});
elements=[element('Old answer')];
const context={
  window:{postMessage(){},addEventListener(){}},
  document:{documentElement:{},querySelectorAll(selector){
    if(selector==='[data-message-author-role="assistant"]')return elements;
    if(selector==='button,[role="button"]')return voice?[{getAttribute:()=> 'End voice chat',textContent:''}]:[];
    return [];
  }},
  chrome:{runtime:{id:'test',sendMessage:async message=>{if(message.kind==='event')events.push(message.event);return{ok:true,instanceId:'one'};}}},
  MutationObserver:class{constructor(cb){observer=cb;}observe(){}disconnect(){}},
  Date:{now:()=>now},crypto:require('node:crypto'),location:{origin:'https://chatgpt.com'},
  setTimeout(cb){const id=nextTimer++;timers.set(id,cb);return id;},clearTimeout(id){timers.delete(id);},setInterval(){return 1;},clearInterval(){},addEventListener(){},Map,WeakMap,
};
vm.createContext(context);
vm.runInContext(fs.readFileSync(process.argv[2]+'/turn-tracker.js','utf8'),context);
vm.runInContext(fs.readFileSync(process.argv[2]+'/content.js','utf8'),context);
function mutate(){observer();for(const[id,cb]of [...timers]){timers.delete(id);cb();}}
elements.push(element('First reply part.'));mutate();
const starts=events.filter(e=>e.type==='start').length;
elements=[element('Old answer'),element('First reply part. More words.')];mutate();
assert.equal(events.filter(e=>e.type==='start').length,starts);
assert.equal(events.filter(e=>e.type==='interrupt').length,0);
assert.equal(events.filter(e=>e.type==='text').at(-1).text,'First reply part. More words.');
voice=false;mutate();now+=100;mutate();voice=true;mutate();
assert.equal(events.filter(e=>e.type==='stop').length,0);
elements.push(element('Another real response.'));mutate();
assert.equal(events.filter(e=>e.type==='start').length,starts+1);
console.log('PASS: DOM replacement keeps one turn; brief missing controls do not stop speech; real new reply starts once');
