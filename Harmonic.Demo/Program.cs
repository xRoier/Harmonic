using Harmonic.Hosting;
using System.Net;
using Harmonic.Demo;

var server = new RtmpServerBuilder()
    .UseStartup<Startup>()
    .UseWebSocket(c =>
    {
        c.BindEndPoint = new IPEndPoint(IPAddress.Parse("0.0.0.0"), 8080);
    })
    .Build();
await server.StartAsync();