namespace AiLight.Core;

// The platform shells only supply discovery; effect intent and connection
// animation use the same controller on Windows and macOS.
public class RobotDevice(IRobotDiscovery discovery, TimeProvider? timeProvider = null) : IDisposable {
    private readonly RobotConnection connection = new(discovery);
    private readonly FirmwareOutput firmwareOutput = new();
    private readonly TimeProvider time = timeProvider ?? TimeProvider.System;
    private long generation;
    private long? animationStarted;
    public string Status => connection.Status;
    public string Id => connection.Id;
    public bool Connected => connection.Connected;
    public DeviceConnectionState State => connection.State;
    public async Task TickAsync(string port,string id,(int R,int G,int B) rgb,int brightness,OutputMode mode,CancellationToken stop) {
        string command=firmwareOutput.Describe(mode,brightness,rgb);
        if(mode is OutputMode.Red or OutputMode.Test)animationStarted=null;
        if(animationStarted is long started) {
            double elapsed=time.GetElapsedTime(started).TotalSeconds;
            if(elapsed<ConnectionAnimation.DurationSeconds) {
                rgb=ConnectionAnimation.Render(rgb,brightness,elapsed);
                command=rgb==(0,0,0)?"OFF":$"SET_RGB {rgb.R} {rgb.G} {rgb.B}";
            }else animationStarted=null;
        }
        await connection.TickAsync(port,id,command,time.GetUtcNow().UtcDateTime,stop);
        if(connection.Generation!=generation) {
            generation=connection.Generation;
            animationStarted=mode is OutputMode.Red or OutputMode.Test?null:time.GetTimestamp();
        }
    }
    public void Dispose()=>connection.Dispose();
}
