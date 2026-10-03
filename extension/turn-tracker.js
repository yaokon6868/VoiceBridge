(() => {
  class TurnTracker {
    constructor(){this.key=null;this.text='';this.userTurn=null;this.baselines=new Map();}
    baseline(key,text){this.baselines.set(key,text);if(this.baselines.size>100)this.baselines.delete(this.baselines.keys().next().value);}
    reset(){this.key=null;this.text='';this.userTurn=null;this.baselines.clear();}
    update(key,text,userTurn=null){
      const before=this.baselines.get(key);
      if(!text || before===text)return null;
      if(before && text.startsWith(before))text=text.slice(before.length).trim();
      if(!text)return null;
      // A streamed reply can acquire a permanent DOM ID when it is committed.
      // Preserve its submission cursor across that identity change. A new user
      // message explicitly permits even an identical answer to be read again.
      const sameUser=userTurn!==null && this.userTurn===userTurn;
      const remounted=sameUser && this.key!==key && this.text.length>=24 &&
        (text===this.text || text.startsWith(this.text));
      const newTurn=this.key!==key && !remounted;
      const changed=newTurn || this.text!==text;
      this.key=key;this.text=text;this.userTurn=userTurn;
      return {newTurn,changed,text};
    }
  }
  globalThis.VoiceBridgeTurnTracker=TurnTracker;
})();
