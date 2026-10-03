(() => {
  if (window.__voiceBridgeHooked) return;
  window.__voiceBridgeHooked = true;
  const emit = (detail) => window.postMessage({ source: "voice-bridge-page", ...detail }, "*");
  let aecReadyUntil=0;
  let aecEnabled=false;
  const micRoutes=new Set();
  window.addEventListener('message',e=>{
    if(e.source===window && e.origin===location.origin && e.data?.source==='voice-bridge-aec'){
      aecReadyUntil=e.data.ready?Date.now()+6000:0;
      aecEnabled=!!e.data.enabled || !!e.data.ready;
      for(const route of micRoutes)route.update();
    }
  });
  setInterval(()=>{for(const route of micRoutes)route.update();},500);
  const media=globalThis.navigator?.mediaDevices;
  if(media?.getUserMedia){
    const original=media.getUserMedia.bind(media);
    media.getUserMedia=async constraints=>{
      try{
        let stream=await original(constraints);
        const isCable=track=>/^CABLE Output \(VB-Audio Virtual Cable\)$/i.test(track.label);
        // A previous preview may have left Chrome's default on the virtual mic.
        // Acquire a real fallback before using the cleaned stream; never return
        // an unpowered virtual track when the helper is absent.
        if(constraints?.audio && stream.getAudioTracks().some(isCable)){
          const devices=await media.enumerateDevices();
          const physical=devices.find(d=>d.kind==='audioinput' && !['default','communications'].includes(d.deviceId) && /microphone|麦克风/i.test(d.label) && !/cable|virtual/i.test(d.label));
          if(!physical)throw new Error('Physical microphone not found');
          const replacement=await original({...constraints,audio:{deviceId:{exact:physical.deviceId},echoCancellation:true}});
          stream.getTracks().forEach(t=>t.stop());stream=replacement;
        }
        if(constraints?.audio && aecEnabled && window.AudioContext){
          const devices=await media.enumerateDevices();
          const cable=devices.find(d=>d.kind==='audioinput' && /^CABLE Output \(VB-Audio Virtual Cable\)$/i.test(d.label));
          if(cable){
            let ctx,virtual,source,closed=false,busy=false,usingVirtual=false,retryAt=0;
            try{
              ctx=new window.AudioContext();await ctx.resume();
              if(ctx.state!=='running')throw new Error('Audio graph did not start');
              const destination=ctx.createMediaStreamDestination();
              const physical=stream;
              const connect=input=>{source?.disconnect();source=ctx.createMediaStreamSource(input);source.connect(destination);};
              connect(physical);
              const report=()=>emit({kind:'microphone-status',state:'live',virtual:usingVirtual,
                label:usingVirtual?'CABLE Output (VB-Audio Virtual Cable)':physical.getAudioTracks()[0]?.label});
              const route={async update(){
                if(closed || busy)return;
                if(Date.now()>=aecReadyUntil){
                  if(usingVirtual){connect(physical);usingVirtual=false;virtual?.getTracks().forEach(t=>t.stop());virtual=null;report();}
                  return;
                }
                if(usingVirtual || Date.now()<retryAt)return;
                busy=true;
                try{
                  const next=await original({audio:{deviceId:{exact:cable.deviceId},autoGainControl:false,echoCancellation:false,noiseSuppression:false}});
                  if(closed || Date.now()>=aecReadyUntil){next.getTracks().forEach(t=>t.stop());return;}
                  virtual=next;connect(next);usingVirtual=true;report();
                  next.getAudioTracks()[0]?.addEventListener('ended',()=>{
                    if(!closed && usingVirtual){connect(physical);usingVirtual=false;retryAt=Date.now()+2000;report();}
                  },{once:true});
                }catch{retryAt=Date.now()+2000;emit({kind:'microphone-status',state:'physical-fallback',virtual:false});}
                finally{busy=false;}
              }};
              const output=destination.stream;
              for(const video of physical.getVideoTracks())output.addTrack(video);
              const cleanup=()=>{if(closed)return;closed=true;micRoutes.delete(route);source?.disconnect();
                physical.getTracks().forEach(t=>t.stop());virtual?.getTracks().forEach(t=>t.stop());ctx.close();};
              for(const track of output.getAudioTracks()){
                const stop=track.stop.bind(track);track.stop=()=>{cleanup();stop();};
                track.addEventListener('ended',cleanup,{once:true});
              }
              micRoutes.add(route);await route.update();return output;
            }catch{virtual?.getTracks().forEach(t=>t.stop());ctx?.close();emit({kind:'microphone-status',state:'physical-fallback',virtual:false});}
          }else emit({kind:'microphone-status',state:'virtual-input-not-found'});
        }
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
