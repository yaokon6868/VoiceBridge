(() => {
  if(globalThis.__voiceBridgeNextContent)return;
  globalThis.__voiceBridgeNextContent=true;
  let active = false, connected = false, sessionId = '', lastSnapshot = '', currentNode = null;
  let networkTurn = false, networkAt = 0, turnId = '', domTimer, completedText = '';
  let hookReady=false, textEvents=0, hookInfo={};
  let microphone={};
  const tracker = new globalThis.VoiceBridgeTurnTracker();
  let missingVoiceAt=0,serverInstance='';
  const identity=(el,index)=>el.closest('[data-message-id]')?.getAttribute('data-message-id') || `assistant-position:${index}`;
  const userTurn=()=>{
    const users=[...document.querySelectorAll('[data-message-author-role="user"]')];
    const headings=users.length?[]:[...document.querySelectorAll('h1,h2,h3,h4,h5,h6,[role="heading"]')].filter(el=>/^(你说|You said)\s*[:：]?$/.test((el.textContent||'').trim()));
    const nodes=users.length?users:headings,last=nodes.at(-1);
    return `${nodes.length}:${last?.closest('[data-message-id]')?.getAttribute('data-message-id')||last?.textContent||''}`;
  };
  const candidates = () => {
    const marked=[...document.querySelectorAll('[data-message-author-role="assistant"]')];
    if(marked.length)return marked;
    return [...document.querySelectorAll('h1,h2,h3,h4,h5,h6,[role="heading"]')]
      .filter(el=>/^(ChatGPT 说|ChatGPT said|ChatGPT says)\s*[:：]?$/.test((el.textContent||'').trim()));
  };
  function assistantText(el) {
    if(el.matches('[data-message-author-role="assistant"]'))return (el.innerText||'').trim();
    // Voice conversations currently expose speaker headings instead of author attributes.
    const range=document.createRange();range.setStartAfter(el);
    const headings=[...document.querySelectorAll('h1,h2,h3,h4,h5,h6,[role="heading"]')];
    const next=headings.find(node=>(el.compareDocumentPosition(node)&Node.DOCUMENT_POSITION_FOLLOWING) && /^(你说|You said|ChatGPT 说|ChatGPT said|ChatGPT says)\s*[:：]?$/.test((node.textContent||'').trim()));
    if(next)range.setEndBefore(next);
    else range.setEndAfter(el.closest('main')||document.body.lastElementChild);
    const fragment=range.cloneContents();
    fragment.querySelectorAll('button,[role="button"],nav,aside,script,style,textarea,[aria-live]').forEach(node=>node.remove());
    const container=document.createElement('div');container.append(fragment);
    return (container.textContent||'').split(/最新一条回复|Latest response|回答已完成/)[0].trim();
  }
  let disposed=false, observer, heartbeatTimer;
  function disposeBridge() {
    if(disposed)return;
    disposed=true; connected=false;
    globalThis.__voiceBridgeNextContent=false;
    clearTimeout(domTimer);clearInterval(heartbeatTimer);observer?.disconnect();
    window.postMessage({source:'voice-bridge-control',muted:false},location.origin);
  }
  async function transmit(event) {
    if(disposed)return false;
    try {
      const r=await chrome.runtime.sendMessage({kind:'event',event});
      connected=!!r?.ok;mute();return connected;
    } catch(error) {
      connected=false;mute();
      if(!chrome.runtime?.id || /context invalidated/i.test(String(error)))disposeBridge();
      return false;
    }
  }  function send(type, text=null, mode='snapshot') {
    return transmit({type,sessionId,text,mode,clientTimestamp:Date.now()});
  }
  function mute() {
    // The background owns tab muting and checks server synthesis readiness.
    window.postMessage({source:'voice-bridge-control',muted:false}, location.origin);
    if(!disposed)chrome.runtime.sendMessage({kind:'mute',enabled:active && connected}).catch(()=>{});
  }
  function beginTurn(id='') {
    // A new start already supersedes the old turn in the desktop pipeline.
    sessionId=crypto.randomUUID(); turnId=id; lastSnapshot=''; completedText='';
    send('start');
  }
  function setActive(value) {
    if(value===active)return;
    active=value;
    if(active) {
      tracker.reset();
      candidates().forEach((el,index)=>tracker.baseline(identity(el,index),assistantText(el)));
      currentNode=null;networkTurn=false;beginTurn();
    } else { send('stop'); sessionId=''; networkTurn=false; }
    mute();
  }
  function voiceActive() {
    return [...document.querySelectorAll('button,[role="button"]')].some(el => {
      const label=`${el.getAttribute('aria-label')||''} ${el.getAttribute('data-testid')||''} ${el.textContent||''}`;
      return /end voice|stop voice|leave voice|end call|结束语音|停止语音|退出语音|结束通话|结束聊天|结束对话/i.test(label);
    });
  }
  function readDom() {
    const detected=voiceActive();
    if(detected)missingVoiceAt=0;
    else if(!missingVoiceAt)missingVoiceAt=Date.now();
    if(detected && !active)setActive(true);
    if(!detected && active && Date.now()-missingVoiceAt>5000 && Date.now()-networkAt>5000){setActive(false);return;}
    if(!active || networkTurn)return;
    const elements=candidates();
    const el=elements.at(-1);
    if(!el)return;
    const update=tracker.update(identity(el,elements.length-1),assistantText(el),userTurn());
    if(!update)return;
    const text=update.text;
    if(update.newTurn){currentNode=el;beginTurn();}
    if(update.changed){lastSnapshot=text;textEvents++;send('text',text);}
    // A quiet fallback in the desktop pipeline handles missing completion events.
    const completed=[...document.querySelectorAll('[aria-live],[role="status"]')].some(node=>/^(回答已完成|Response completed|Response complete)$/.test((node.textContent||'').trim()));
    if(text && completed && completedText!==text){completedText=text;send('complete');}
  }
  window.addEventListener('message',e=>{
    if(disposed || e.source!==window || e.origin!==location.origin || e.data?.source!=='voice-bridge-page')return;
    const msg=e.data;
    if(msg.kind==='microphone-status'){microphone=msg;return;}
    if(msg.kind==='hook-ready'){hookReady=true;hookInfo=msg;return;}
    networkAt=Date.now();
    if(msg.kind==='network-delta' || (msg.kind==='network-snapshot' && active)) {
      if(!active)setActive(true);
      if(!sessionId || !networkTurn || (msg.turnId && msg.turnId!==turnId))beginTurn(msg.turnId||'');
      networkTurn=true;
      if(typeof msg.text==='string'){textEvents++;send('text',msg.text,msg.kind==='network-snapshot'?'snapshot':'delta');}
      if(msg.complete)send('complete');
    } else if(msg.kind==='interrupt' && active){
      send('interrupt');sessionId='';networkTurn=false;currentNode=null;
      tracker.reset();candidates().forEach((el,index)=>tracker.baseline(identity(el,index),assistantText(el)));
    }
    else if(msg.kind==='network-stop' && active && networkTurn)send(msg.eventType==='response.cancelled'?'interrupt':'complete');
  });
  const observe=()=>{
    observer=new MutationObserver(()=>{if(!domTimer)domTimer=setTimeout(()=>{domTimer=null;readDom();},100);})
      ;observer.observe(document.documentElement,{subtree:true,childList:true,characterData:true,attributes:true,attributeFilter:['aria-label','data-testid']});
    readDom();
  };
  if(document.documentElement)observe();else addEventListener('DOMContentLoaded',observe,{once:true});
  async function heartbeat(){
    if(disposed)return;
    try{
      const health=await chrome.runtime.sendMessage({kind:'health'});
      connected=!!health?.ok;
      if(connected && health.instanceId && serverInstance && health.instanceId!==serverInstance && active){
        setActive(false);setActive(true); // Rebaseline; never replay old text after a bridge restart.
      }
      if(connected)serverInstance=health.instanceId||serverInstance;
      window.postMessage({source:'voice-bridge-aec',ready:connected && health?.aecReady===true,enabled:connected && health?.aecEnabled===true},location.origin);
    }catch(error){connected=false;window.postMessage({source:'voice-bridge-aec',ready:false},location.origin);if(!chrome.runtime?.id || /context invalidated/i.test(String(error))){disposeBridge();return;}}
    window.postMessage({source:'voice-bridge-probe'},location.origin);
    mute();readDom();
    if(connected) transmit({type:'web-status',sessionId:sessionId||'idle',text:JSON.stringify({version:'0.3.1',hookReady,active,networkTurn,textEvents,assistantNodes:candidates().length,hookInfo,microphone})});
  }
  heartbeatTimer=setInterval(heartbeat,2500);heartbeat();
  addEventListener('pagehide',()=>{if(active){send('stop');active=false;mute();}});
})();
