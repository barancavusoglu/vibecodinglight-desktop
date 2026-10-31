using AiLight.Core;
using System.Text.Json;

public static class ParityRegression {
    public static async Task Run(Action<bool,string> check) {
        string[] macPorts = ["/dev/cu.Bluetooth-Incoming-Port", "/dev/tty.Bluetooth-Incoming-Port", "/dev/cu.debug-console", "/dev/tty.debug-console", "/dev/cu.usbmodem23101", "/dev/tty.usbmodem23101"];
        var choices = DevicePortChoice.Create(macPorts, "", true);
        check(choices.Length == 2 && choices[1].Address == "/dev/cu.usbmodem23101", "Mac device choices omit system ports and merge cu/tty aliases without probing hardware");
        check(choices[0].Address == "" && choices[1].Label == "USB · 23101" && !choices.Any(c => c.Label.Contains("/dev/")), "Mac device names are readable while selection retains the real address");
        var tty = DevicePortChoice.Create(macPorts, "/dev/tty.usbmodem23101", true);
        check(tty.Length == 2 && DevicePortChoice.Matches(tty[1].Address, "/dev/tty.usbmodem23101", true), "Existing tty selection remains selected without duplicate USB entries or settings mutation");
        var unplugged = DevicePortChoice.Create([], "/dev/cu.usbmodemOLD", true);
        check(unplugged.Length == 2 && unplugged[1].Address == "/dev/cu.usbmodemOLD" && unplugged[1].Label.Contains("bağlı değil"), "Unplugged user-selected address is retained and clearly marked");
        check(DevicePortChoice.Create([], "", true).Length == 1, "Empty device discovery keeps automatic selection available");
        var windowsPorts = DevicePortChoice.Create(["COM1", "COM5", "COM5"], "COM5", false);
        check(windowsPorts.Length == 3 && windowsPorts[2].Address == "COM5" && windowsPorts[2].Label == "COM5", "Windows retains its COM labels and exact transport addresses");
        check(DevicePortChoice.Create(["/dev/cu.usbmodem1", "/dev/cu.usbmodem2", "BLE:ABCDEF123456"], "", true).Length == 4, "Multiple USB devices and discovered Bluetooth devices remain individually selectable");
        var connectedBle = DevicePortChoice.Create([], "", true, "BLE:ABCDEF123456");
        check(connectedBle.Length == 2 && connectedBle[1].Address == "BLE:ABCDEF123456" && connectedBle[1].Label.Contains("bağlı"),
            "Active Bluetooth device stays selectable when scanning stops and the advertisement cache expires");
        check(DevicePortChoice.Create(["BLE:ABCDEF123456", "BLE:BBBBBB654321"], "", true, "BLE:ABCDEF123456").Length == 3,
            "Current device merges with discovery without hiding a second robot or adding duplicates");
        foreach(string wire in new[]{"null","{}","[]","{", "{\"Id\":\"x\",\"Harness\":\"codex\",\"SessionId\":\"s\",\"MonitorRoot\":null}"})
            check(HookWire.Read(wire) is null,"Malformed/partial IPC cannot crash either runtime or start monitoring");
        var disconnect=new HookEvent("disconnect","opencode","","OpenCodeDisconnected","","","","","",false,1,ProducerId:"producer");
        check(HookWire.Read(JsonSerializer.Serialize(disconnect)) is not null,"Producer-exit IPC can close OpenCode sessions without a session ID");
        var greenIcon=RobotIcon.Pixels(OutputMode.Green);var redIcon=RobotIcon.Pixels(OutputMode.Red);
        int eyeOffset=(18*32+12)*4;
        check(greenIcon.Length==4096&&greenIcon[3]==0&&greenIcon[eyeOffset+1]>greenIcon[eyeOffset+2],"Shared tray icon has transparent corners and working-green eyes");
        check(redIcon[eyeOffset+2]>redIcon[eyeOffset+1],"Both OS status icons use the same red state pixels");
        check(DesktopThemes.Normalize("sage")=="light"&&DesktopThemes.Normalize("light")=="light","Existing Windows/Mac light-theme settings migrate to the same canonical choice");
        check(DesktopThemes.Normalize("ocean")=="mist"&&DesktopThemes.Normalize("lavender")=="mist","Legacy theme aliases retain their Windows palette on both OSes");
        var observations=new ConnectionObservations();
        observations.Observe("codex");observations.Checked("codex","a");
        check(observations.Has("codex"),"Event arriving before the first health check remains observed");
        observations.Checked("codex","a");check(observations.Has("codex"),"Unchanged registration keeps its observed state");
        observations.Checked("codex","b");check(!observations.Has("codex"),"Changed registration requires a new real event");
        observations.Observe("codex");observations.Reset("codex");check(!observations.Has("codex"),"Explicit repair resets prior event confirmation");
        check(SettingSteps.DurationLabel(5)=="5 dakika"&&SettingSteps.DurationLabel(120)=="2 saat","Both shells share duration labels");
        var discovery=new DeviceDiscovery();var time=new DeviceTime();
        using(var device=new RobotDevice(discovery,time)) {
            await device.TickAsync("","",(0,255,0),100,OutputMode.Green,CancellationToken.None);
            time.Advance(TimeSpan.FromMilliseconds(500));
            await device.TickAsync("","",(0,255,0),100,OutputMode.Green,CancellationToken.None);
            check(discovery.Link.Commands.Last().StartsWith("SET_RGB "),"Both platform device wrappers play the same verified-connection animation");
            time.Advance(TimeSpan.FromMilliseconds(50));
            await device.TickAsync("","",(255,0,0),100,OutputMode.Red,CancellationToken.None);
            check(discovery.Link.Commands.Last()=="SET_EFFECT SOLID 255 0 0","Real permission red interrupts connection animation immediately");
            time.Advance(TimeSpan.FromMilliseconds(50));
            await device.TickAsync("","",(10,20,30),100,OutputMode.Test,CancellationToken.None);
            check(discovery.Link.Commands.Last()=="SET_RGB 10 20 30","Manual RGB preview is never altered by connection animation");
        }
        string root=Path.Combine(Path.GetTempPath(),"vcl-parity-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        try {
            string index=Path.Combine(root,"session_index.jsonl");
            File.WriteAllText(index,"{\"id\":\"visible\",\"thread_name\":\"Visible title\"}\n");
            var catalog=new SessionCatalog(()=>index);catalog.Initialize();
            var engine=new StateEngine{IsVisible=catalog.IsVisible};
            HookEvent E(string id,string name)=>new(Guid.NewGuid().ToString(),"codex",id,name,"t","q","request_user_input","project","",false,1);
            engine.Apply(E("hidden","UserPromptSubmit"));engine.Apply(E("hidden","PreToolUse"));
            check(engine.Aggregate==Activity.Idle&&!engine.HasIdleSession,"Catalog-hidden questions do not influence either shell's light");
            engine.Apply(E("visible","UserPromptSubmit"));catalog.Refresh(engine.Sessions);
            var wait=DateTime.UtcNow.AddSeconds(3);
            while(catalog.Get(engine.Sessions.Single(s=>s.Key=="codex:visible"))!="Visible title"&&DateTime.UtcNow<wait)await Task.Delay(20);
            check(catalog.Get(engine.Sessions.Single(s=>s.Key=="codex:visible"))=="Visible title","Shared catalog resolves exact user-facing titles");
            var exec=engine.Sessions.Single(s=>s.Key=="codex:hidden");exec.OneShot=true;
            check(catalog.IsVisible(exec),"One-shot Codex sessions remain visible without a catalog entry");
            File.Delete(index);catalog.Initialize();exec.OneShot=false;
            check(catalog.IsVisible(exec),"Unavailable optional catalog does not hide all sessions");
            var journal=new DiagnosticJournal();
            var state=DiagnosticJournal.Capture(engine);
            for(int i=0;i<510;i++)journal.Record("UNKNOWN_PRIVATE_EVENT","unknown","SECRET_SESSION",state,state,70,false,false,"SECRET_REQUEST","SECRET_COMMAND","SECRET_TURN","SECRET_SOURCE");
            string serialized=JsonSerializer.Serialize(journal.Snapshot());
            check(journal.Snapshot().Length==500,"Both shells bound the privacy-minimal journal to 500 entries");
            check(!serialized.Contains("SECRET")&&journal.Snapshot().All(e=>e.Event=="OtherEvent"&&e.Harness=="app"&&e.Source==""),"Diagnostics never persist raw IDs, commands or unrecognized event/source strings");
            journal.Clear();check(journal.Snapshot().Length==0,"Clearing diagnostics does not touch session state");await journal.Complete();
            string sessions=Path.Combine(root,"sessions");Directory.CreateDirectory(sessions);
            string file=Path.Combine(sessions,"review.jsonl");
            string meta="{\"type\":\"session_meta\",\"payload\":{\"session_id\":\"review\",\"parent_thread_id\":\"parent\",\"thread_source\":\"guardian_review\"}}\n";
            string Result(string turn)=>JsonSerializer.Serialize(new { type="response_item",payload=new { type="message",phase="final_answer",content=new[]{new {type="output_text",text="{\"risk_level\":\"low\",\"user_authorization\":\"high\",\"outcome\":\"allow\",\"rationale\":\"PRIVATE\"}"}},internal_chat_message_metadata_passthrough=new{turn_id=turn}}})+"\n";
            File.WriteAllText(file,meta+Result("old"));
            var seen=new System.Collections.Concurrent.ConcurrentQueue<string>();
            var ready=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var started=new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            var resolved=new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var cancel=new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var watcher=new CodexApprovalWatcher(sessions);
            var task=watcher.Run((id,outcome)=>{seen.Enqueue(id+":"+outcome);resolved.TrySetResult(id+":"+outcome);},cancel.Token,status:_=>ready.TrySetResult(true),started:id=>started.TrySetResult(id));
            try {
                await ready.Task.WaitAsync(cancel.Token);
                check(seen.IsEmpty,"Approval watcher skips historical decisions on startup");
                File.AppendAllText(file,"{\"type\":\"event_msg\",\"payload\":{\"type\":\"task_started\"}}\n");
                check(await started.Task.WaitAsync(cancel.Token)=="parent","Live review start maps to the parent session");
                string result=Result("fresh");int half=result.Length/2;File.AppendAllText(file,result[..half]);
                await Task.Delay(350,cancel.Token);check(seen.IsEmpty,"Incomplete approval record cannot resolve a permission");
                File.AppendAllText(file,result[half..]);
                check(await resolved.Task.WaitAsync(cancel.Token)=="parent:allow","Shared watcher delivers a live auto-review outcome to the correct parent");
            } finally {cancel.Cancel();await task;}
            check(task.IsCompletedSuccessfully,"Approval watcher exits cleanly on cancellation");
        } finally {Directory.Delete(root,true);}
    }
    private sealed class DeviceTime:TimeProvider {
        private long ticks;
        public override long TimestampFrequency=>TimeSpan.TicksPerSecond;
        public override long GetTimestamp()=>ticks;
        public override DateTimeOffset GetUtcNow()=>new DateTimeOffset(2026,1,1,0,0,0,TimeSpan.Zero).AddTicks(ticks);
        public void Advance(TimeSpan elapsed)=>ticks+=elapsed.Ticks;
    }
    private sealed class DeviceLink:IRobotLink {
        public List<string> Commands {get;}=[];
        public string Address=>"USB:test";public bool IsUsb=>true;
        public Task<string> RequestAsync(string command,CancellationToken stop){Commands.Add(command);return Task.FromResult(command=="INFO"?"AI_LIGHT PROTOCOL=1 FW=0.3.0 ID=001122AABBCC CAPS=RGB8,WATCHDOG,FX1 WATCHDOG_MS=6000 READY=1":command=="PING"?"PONG":"OK");}
        public void Dispose(){}
    }
    private sealed class DeviceDiscovery:IRobotDiscovery {
        public DeviceLink Link {get;}=new();public string BluetoothStatus=>"test";
        public IReadOnlyList<string> UsbCandidates()=>[Link.Address];public IReadOnlyList<string> BleCandidates()=>[];
        public Task<IRobotLink> OpenAsync(string address,CancellationToken stop)=>Task.FromResult<IRobotLink>(Link);
        public void Dispose(){}
    }
}
