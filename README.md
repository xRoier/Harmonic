# Harmonic
A high performance RTMP Server framework implementation


# Getting started

## Code

Program.cs

```csharp
using Harmonic.Hosting;
using System;
using System.Net;

RtmpServer server = new RtmpServerBuilder()
    .UseStartup<Startup>()
    .Build();
await server.StartAsync();
```

Startup.cs
```csharp
using Autofac;
using Harmonic.Hosting;

namespace Harmonic.Demo;

class Startup : IStartup
{
    public void ConfigureServices(ContainerBuilder builder)
    {
    }
}
```

Build a server like this to support websocket-flv transmission

```csharp
RtmpServer server = new RtmpServerBuilder()
    .UseStartup<Startup>()
    .UseWebSocket(c =>
    {
        c.BindEndPoint = new IPEndPoint(IPAddress.Parse("0.0.0.0"), 8080);
    })
    .Build();

```

## push video file using ffmpeg
```bash
ffmpeg -i test.mp4 -f flv -vcodec h264 -acodec aac "rtmp://127.0.0.1/living/streamName"
```
## play rtmp stream using ffplay

```bash
ffplay "rtmp://127.0.0.1/living/streamName"
```

## play flv stream using [flv.js](https://github.com/Bilibili/flv.js) by websocket

```html
<video id="player"></video>

<script>

    if (flvjs.isSupported()) {
        var player = document.getElementById('player');
        var flvPlayer = flvjs.createPlayer({
            type: 'flv',
            url: "ws://127.0.0.1/websocketplay/streamName"
        });
        flvPlayer.attachMediaElement(player);
        flvPlayer.load();
        flvPlayer.play();
    }
</script>
```


# Dive in deep
You can view docs [here](docs/README.md)
