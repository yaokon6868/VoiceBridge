(async()=>{
  document.querySelector('#reconnect').onclick=async()=>{
    const result=await chrome.runtime.sendMessage({kind:'reconnect'}).catch(()=>null);
    document.querySelector('#repair').textContent=result?.ok?`已接入 ${result.count} 个网页；请说一句新话验证。`:'接入失败，请刷新 ChatGPT 网页。';
  };
  try{
    const read=async path=>{const r=await chrome.runtime.sendMessage({kind:path});if(!r?.ok)throw Error();return r;};
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
  }catch{document.querySelector('#status').textContent='Next 未连接。原版使用另一个端口，不受影响。';}
})();
