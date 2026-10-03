const queues = new Map();
let sessionToken='',pairing=null;
async function bridgeRequest(path,options={}) {
  const connect=()=>pairing ||= fetch('http://127.0.0.1:17892/session',{
    method:'POST',headers:{'X-VoiceBridge-Client':'extension'},signal:AbortSignal.timeout(2500)
  }).then(async r=>{
    // Migration only: the user's already-running preview predates /session.
    // Never downgrade after an authorization failure from a current server.
    if(r.status===404){
      const health=await fetch('http://127.0.0.1:17892/health',{signal:AbortSignal.timeout(2500)});
      const info=health.ok?await health.json():null;
      if(info?.service==='VoiceBridge Next' && info.version==='0.3.0-preview' && info.instanceId){sessionToken='legacy-preview';return;}
    }
    if(!r.ok)throw Error('Bridge pairing failed');sessionToken=(await r.json()).token;
    if(typeof sessionToken!=='string' || !sessionToken)throw Error('Invalid bridge session');
  }).finally(()=>{pairing=null;});
  for(let attempt=0;attempt<2;attempt++){
    if(!sessionToken)await connect();
    const r=await fetch(`http://127.0.0.1:17892/${path}`,{...options,
      headers:{...options.headers,...(sessionToken==='legacy-preview'?{}:{Authorization:`Bearer ${sessionToken}`})},signal:AbortSignal.timeout(2500)});
    if(r.status===401){sessionToken='';continue;}
    return r;
  }
  throw Error('Bridge authorization failed');
}
async function reconnectPages() {
  const tabs=await chrome.tabs.query({url:['https://chatgpt.com/*','https://chat.openai.com/*']});
  let attached=0;
  for(const tab of tabs)try{
    await chrome.scripting.executeScript({target:{tabId:tab.id},files:['page-hook.js'],world:'MAIN'});
    await chrome.scripting.executeScript({target:{tabId:tab.id},files:['turn-tracker.js','content.js']});
    attached++;
  }catch{}
  return attached;
}
chrome.runtime.onInstalled?.addListener(()=>reconnectPages().catch(()=>{}));
chrome.runtime.onStartup?.addListener(()=>reconnectPages().catch(()=>{}));
let muteQueue=Promise.resolve();
function setTabMute(id,enabled) {
  muteQueue=muteQueue.catch(()=>{}).then(async()=>{
    const key=`mute-${id}`;
    const stored=(await chrome.storage.session.get(key))[key];
    const tab=await chrome.tabs.get(id).catch(()=>null);
    if(!tab){await chrome.storage.session.remove(key);return;}
    if(enabled){
      if(!stored && tab.mutedInfo?.muted)return; // Respect pre-existing user mute.
      await chrome.storage.session.set({[key]:{id,expires:Date.now()+15000}});
      if(!tab.mutedInfo?.muted)await chrome.tabs.update(id,{muted:true});
    }else if(stored){
      if(tab.mutedInfo?.extensionId===chrome.runtime.id)await chrome.tabs.update(id,{muted:false});
      await chrome.storage.session.remove(key);
    }
  });
  return muteQueue;
}
chrome.alarms.create('voice-mute-watchdog',{periodInMinutes:0.5});
chrome.alarms.onAlarm.addListener(async alarm=>{
  if(alarm.name!=='voice-mute-watchdog')return;
  const states=await chrome.storage.session.get(null);
  for(const [key,value] of Object.entries(states))if(key.startsWith('mute-') && value.expires<Date.now())await setTabMute(value.id,false);
});
chrome.tabs.onUpdated.addListener((id,change)=>{if(change.status==='loading')setTabMute(id,false).catch(()=>{});});
chrome.tabs.onRemoved.addListener(id=>{chrome.storage.session.remove(`mute-${id}`).catch(()=>{});});
chrome.runtime.onMessage.addListener((message, sender, reply) => {
  if(!sender.tab && sender.id===chrome.runtime.id && ['health','diagnostics'].includes(message.kind)){
    bridgeRequest(message.kind).then(async r=>{if(!r.ok)throw Error();reply({ok:true,...await r.json()});}).catch(()=>reply({ok:false}));
    return true;
  }
  if(message.kind==='reconnect' && !sender.tab && sender.id===chrome.runtime.id){
    reconnectPages().then(count=>reply({ok:true,count}),()=>reply({ok:false}));
    return true;
  }
  if (!sender.tab || !/^https:\/\/(chatgpt\.com|chat\.openai\.com)\//.test(sender.url || '')) return;
  if(message.kind==='mute'){
    // Never suppress original speech when synthesis is disabled or unconfigured.
    (async()=>{
      let ready=false;
      if(message.enabled)try{
        const r=await bridgeRequest('health');
        ready=r.ok && (await r.json()).ttsReady===true;
      }catch{}
      await setTabMute(sender.tab.id,!!message.enabled && ready);
      reply({ok:true});
    })().catch(()=>reply({ok:false}));
    return true;
  }
  if (message.kind === 'health') {
    bridgeRequest('health')
      .then(r => { if (!r.ok) throw Error(); return r.json(); })
      .then(data => reply({ok:true, ...data})).catch(() => reply({ok:false}));
    return true;
  }
  if (message.kind !== 'event' || !message.event) return;
  const key = sender.tab.id;
  const event = {...message.event, source: `chatgpt-web-${key}`};
  const next = (queues.get(key) || Promise.resolve()).catch(() => {}).then(async () => {
    const r = await bridgeRequest('event', {
      method:'POST', headers:{'Content-Type':'application/json'},
      body:JSON.stringify(event), signal:AbortSignal.timeout(2500)
    });
    if (!r.ok) throw Error('Bridge unavailable');
  });
  queues.set(key, next);
  next.then(() => reply({ok:true}), () => reply({ok:false})).finally(() => {
    if (queues.get(key) === next) queues.delete(key);
  });
  return true;
});
