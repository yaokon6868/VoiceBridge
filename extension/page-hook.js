(() => {
  if (window.__voiceBridgeHooked) return;
  window.__voiceBridgeHooked = true;
  const emit = (detail) => window.postMessage({ source: "voice-bridge-page", ...detail }, "*");
  let aecReadyUntil=0;
  window.addEventListener('message',e=>{
    if(e.source===window && e.origin===location.origin && e.data?.source==='voice-bridge-aec')
      aecReadyUntil=e.data.ready?Date.now()+6000:0;
  });
  const media=globalThis.navigator?.mediaDevices;
  if(media?.getUserMedia){
    const original=media.getUserMedia.bind(media);
    media.getUserMedia=async constraints=>{
      let selected=constraints;
      if(constraints?.audio && Date.now()<aecReadyUntil){
        const devices=await media.enumerateDevices();
        const cable=devices.find(d=>d.kind==='audioinput' && /^CABLE Output \(VB-Audio Virtual Cable\)$/i.test(d.label));
        if(cable){
          selected={...constraints,audio:{...(typeof constraints.audio==='object'?constraints.audio:{}),
            deviceId:{exact:cable.deviceId},autoGainControl:false,echoCancellation:false,noiseSuppression:false}};
        }else emit({kind:'microphone-status',state:'virtual-input-not-found'});
      }
      try{
        const stream=await original(selected);
        for(const track of stream.getAudioTracks()){
          const settings=track.getSettings();
          emit({kind:'microphone-status',state:'live',label:track.label,
            virtual:/^CABLE Output \(VB-Audio Virtual Cable\)$/i.test(track.label),
            autoGainControl:settings.autoGainControl,echoCancellation:settings.echoCancellation,
            noiseSuppression:settings.noiseSuppression});
          track.addEventListener('ended',()=>emit({kind:'microphone-status',state:'ended',label:track.label}),{once:true});
        }
        return stream;
      }catch(error){emit({kind:'microphone-status',state:'failed',error:error.name});throw error;}
    };
  }
  const deltaTypes = new Set([
    "response.output_audio_transcript.delta",
    "response.audio_transcript.delta"
  ]);
  const stopTypes = new Set([
    "response.done", "response.cancelled", "response.audio_transcript.done",
    "response.output_audio_transcript.done"
  ]);
  const observedTypes = new Map();
  let packets=0, channels=0;
  const inspect = (raw, depth = 0) => {
    try {
      if (depth > 8) return;
      if (raw instanceof Blob) { raw.text().then(text => inspect(text, depth + 1)); return; }
      if (raw instanceof ArrayBuffer) { inspect(new TextDecoder().decode(raw), depth + 1); return; }
      const obj = typeof raw === "string" ? JSON.parse(raw) : raw;
      if (!obj || typeof obj !== "object") return;
      if (Array.isArray(obj)) { for (const item of obj) inspect(item, depth + 1); return; }
      packets++;
      const shape = {type:obj.type||obj.event||obj.event_type||'(none)',keys:Object.keys(obj).slice(0,25)};
      for(const key of ['data','payload','message']) {
        if(obj[key] && typeof obj[key]==='object') shape[key]=Object.keys(obj[key]).slice(0,25);
      }
      if(observedTypes.size<30)observedTypes.set(String(shape.type),shape);
      if (deltaTypes.has(obj.type) && typeof obj.delta === "string")
        emit({ kind: "network-delta", text: obj.delta, turnId: obj.response_id || obj.item_id, eventType: obj.type, at: performance.now() });
      else if (stopTypes.has(obj.type)) emit({ kind: "network-stop", eventType: obj.type, at: performance.now() });
      else if ((obj.type === "input_audio_buffer.speech_started" || obj.type === "conversation.interrupted"))
        emit({ kind: "interrupt", eventType: obj.type, at: performance.now() });
      // Only explicitly assistant-authored text is eligible for snapshot playback.
      if (obj.author?.role === 'assistant' && obj.content?.content_type === 'text' && Array.isArray(obj.content.parts)) {
        const text = obj.content.parts.filter(part => typeof part === 'string').join('');
        if (text) emit({kind:'network-snapshot',text,turnId:obj.id,eventType:'assistant-message',complete:obj.status==='finished_successfully',at:performance.now()});
      }
      for (const key of ['data','payload','message','messages','update_content','events']) {
        const value = obj[key];
        if (value && (typeof value === 'object' || (typeof value === 'string' && /^[\[{]/.test(value)))) inspect(value, depth + 1);
      }
    } catch { }
  };

  const NativeWebSocket = window.WebSocket;
  window.WebSocket = new Proxy(NativeWebSocket, {
    construct(Target, args) {
      const socket = new Target(...args);
      socket.addEventListener("message", e => inspect(e.data));
      return socket;
    }
  });

  const observeChannel = channel => {
    if (!channel || channel.__voiceBridgeObserved) return channel;
    channels++;
    channel.__voiceBridgeObserved = true;
    channel.addEventListener("message", e => inspect(e.data));
    return channel;
  };
  if (window.RTCPeerConnection) {
    const NativePeerConnection = window.RTCPeerConnection;
    window.RTCPeerConnection = new Proxy(NativePeerConnection, {
      construct(Target, args) {
        const pc = new Target(...args);
        const nativeCreate = pc.createDataChannel.bind(pc);
        pc.createDataChannel = (...channelArgs) => observeChannel(nativeCreate(...channelArgs));
        pc.addEventListener("datachannel", e => observeChannel(e.channel));
        return pc;
      }
    });
  }

  window.addEventListener('message', e => {
    if(e.source === window && e.origin === location.origin && e.data?.source === 'voice-bridge-probe')
      emit({kind:'hook-ready',version:'0.2.3',packets,channels,types:[...observedTypes.values()]});
  });
  let muting=false;
  const saved=new Map();
  const applyMute=()=>{
    if(!muting)return;
    for(const audio of document.querySelectorAll('audio')){
      if(!saved.has(audio))saved.set(audio,audio.muted);
      if(!audio.muted)audio.muted=true;
    }
  };
  window.addEventListener('message',e=>{
    if(e.source!==window || e.origin!==location.origin || e.data?.source!=='voice-bridge-control')return;
    muting=!!e.data.muted;
    if(muting)applyMute();
    else {for(const [audio,value] of saved)audio.muted=value;saved.clear();}
  });
  setInterval(applyMute,200);
})();
