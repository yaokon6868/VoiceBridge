(async()=>{
  const extensionVersion=chrome.runtime.getManifest().version;
  document.querySelector('b').textContent=`VoiceBridge · 扩展 ${extensionVersion}`;
  document.querySelector('#reconnect').onclick=async()=>{
    const result=await chrome.runtime.sendMessage({kind:'reconnect'}).catch(()=>null);
    document.querySelector('#repair').textContent=result?.ok?`已接入 ${result.count} 个网页；请说一句新话验证。`:'接入失败，请刷新 ChatGPT 网页。';
  };
  try{
    const read=async path=>{const r=await chrome.runtime.sendMessage({kind:path});if(!r?.ok)throw Object.assign(Error(),{code:r?.error||'bridge-unreachable'});return r;};
    const [health,diagnostics]=await Promise.all([read('health'),read('diagnostics')]);
    const entry=Object.values(diagnostics.web||{}).sort((a,b)=>Date.parse(b.at)-Date.parse(a.at))[0];
    const fresh=entry && Date.now()-Date.parse(entry.at)<8000;
    document.querySelector('#status').textContent=!health.ttsReady
      ?'换声暂停：'+(diagnostics.fallbackReason||diagnostics.fishStatus)
      :!fresh?'本机服务正常，但未收到网页心跳。'
      :!entry.data?.active?'网页已接通，尚未检测到正在进行的语音。'
      :'语音已检测；文本事件：'+(entry.data.textEvents||0)+'；播放状态：'+(diagnostics.playback?.state||'idle');
    const mic=entry?.data?.microphone;
    document.querySelector('#microphone').textContent=entry && Date.now()-Date.parse(entry.at)<8000 && mic?.state==='live'
      ?'网页实际输入：'+mic.label:'尚未收到当前网页麦克风状态，请开始语音后查看。';
  }catch(error){
    const reason=error.code==='bridge-unreachable'?'无法连接本机客户端，请确认程序已运行。'
      :error.code?.startsWith('pairing-')?'扩展配对被客户端拒绝，请核对扩展与客户端版本。'
      :'连接认证失败，请重载扩展并重新接入网页。';
    document.querySelector('#status').textContent=reason;
  }
})();
