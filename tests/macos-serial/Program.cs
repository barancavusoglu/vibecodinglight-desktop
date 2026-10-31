using System.IO.Ports;
using System.Diagnostics;
using AiLight.Transport;
if(args.Length!=1 || !args[0].StartsWith("/dev/ttys",StringComparison.Ordinal))return 2;
using var link=new SerialRobotLink(new SerialPort(args[0],115200){ReadTimeout=100,WriteTimeout=150,DtrEnable=false,RtsEnable=false,NewLine="\n"});
Console.WriteLine("READY");
try { await link.RequestAsync(new string('X',1_000_000),CancellationToken.None); }
catch(Exception e) when(e is TimeoutException or IOException) { Console.WriteLine("WRITE_LIMIT"); }
Console.WriteLine("CLOSE_PEER");
Console.ReadLine();
var clock=Stopwatch.StartNew();link.Dispose();
Console.WriteLine("CLOSED "+clock.ElapsedMilliseconds);
return clock.ElapsedMilliseconds<1000?0:1;
