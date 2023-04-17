using Autofac;
using System;
using System.Collections.Generic;
using System.Net;
using System.Text.RegularExpressions;
using SharpRtmp.Controllers;

namespace SharpRtmp.Hosting;

public class WebSocketOptions
{
    public IPEndPoint BindEndPoint { get; set; }
    public Regex UrlMapping { get; set; } = new(@"/(?<controller>[a-zA-Z0-9]+)/(?<streamName>[a-zA-Z0-9\.]+)", RegexOptions.IgnoreCase);

    internal readonly Dictionary<string, Type> Controllers = new();

    internal RtmpServerOptions ServerOptions = null;

    internal void RegisterController<T>() where T: WebSocketController => RegisterController(typeof(T));

    internal void RegisterController(Type controllerType)
    {
        if (!typeof(WebSocketController).IsAssignableFrom(controllerType))
            throw new ArgumentException("controller not inherit from WebSocketController");
        Controllers.Add(controllerType.Name.Replace("Controller", "").ToLower(), controllerType);
        ServerOptions.Builder.RegisterType(controllerType).AsSelf();
    }
}