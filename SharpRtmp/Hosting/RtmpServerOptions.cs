using Autofac;
using System;
using System.Collections.Generic;
using System.Net;
using System.Reflection;
using System.Security.Cryptography.X509Certificates;
using SharpRtmp.Controllers;
using SharpRtmp.Networking.Rtmp;
using SharpRtmp.Networking.Rtmp.Data;
using SharpRtmp.Networking.Rtmp.Messages;
using SharpRtmp.Networking.Rtmp.Messages.Commands;
using SharpRtmp.Networking.Rtmp.Messages.UserControlMessages;
using SharpRtmp.Networking.Rtmp.Serialization;
using SharpRtmp.Rpc;
using SharpRtmp.Service;

namespace SharpRtmp.Hosting;

public class RtmpServerOptions
{
    private readonly Dictionary<MessageType, MessageFactory> _messageFactories = new();
    public IReadOnlyDictionary<MessageType, MessageFactory> MessageFactories => _messageFactories;
    public delegate Message MessageFactory(MessageHeader header, SerializationContext context, out int consumed);
    private readonly Dictionary<string, Type> _registeredControllers = new();
    internal ContainerBuilder Builder;
    private readonly RpcService _rpcService;
    private IStartup _startup;
    internal IStartup Startup
    {
        get => _startup;
        set
        {
            _startup = value;
            Builder = new ContainerBuilder();
            _startup.ConfigureServices(Builder);
            RegisterCommonServices(Builder);
        }
    }

    private IContainer ServiceContainer { get; set; }
    public ILifetimeScope ServerLifetime { get; private set; }

    public IReadOnlyDictionary<string, Type> RegisteredControllers => _registeredControllers;
    public IPEndPoint RtmpEndPoint { get; set; } = new(IPAddress.Any, 1935);
    public bool UseUdp { get; set; } = true;
    public bool UseWebsocket { get; set; } = true;
    public X509Certificate2 Cert { get; set; }

    internal RtmpServerOptions()
    {
        var userControlMessageFactory = new UserControlMessageFactory();
        var commandMessageFactory = new CommandMessageFactory();
        RegisterMessage<AbortMessage>();
        RegisterMessage<AcknowledgementMessage>();
        RegisterMessage<SetChunkSizeMessage>();
        RegisterMessage<SetPeerBandwidthMessage>();
        RegisterMessage<WindowAcknowledgementSizeMessage>();
        RegisterMessage<DataMessage>((MessageHeader header, SerializationContext _, out int consumed) =>
        {
            consumed = 0;
            return new DataMessage(header.MessageType == MessageType.Amf0Data ? AmfEncodingVersion.Amf0 : AmfEncodingVersion.Amf3);
        });
        RegisterMessage<VideoMessage>();
        RegisterMessage<AudioMessage>();
        RegisterMessage<UserControlMessage>(userControlMessageFactory.Provide);
        RegisterMessage<CommandMessage>(commandMessageFactory.Provide);
        _rpcService = new RpcService();
    }

    internal void BuildContainer()
    {
        ServiceContainer = Builder.Build();
        ServerLifetime = ServiceContainer.BeginLifetimeScope();
    }

    public void RegisterMessage<T>(MessageFactory factory) where T : Message
    {
        var tType = typeof(T);
        var attr = tType.GetCustomAttribute<RtmpMessageAttribute>();
        if (attr == null)
            throw new InvalidOperationException();

        foreach (var messageType in attr.MessageTypes)
            _messageFactories.Add(messageType, factory);
    }

    public void RegisterMessage<T>() where T : Message, new()
    {
        var tType = typeof(T);
        var attr = tType.GetCustomAttribute<RtmpMessageAttribute>();
        if (attr == null)
            throw new InvalidOperationException();

        foreach (var messageType in attr.MessageTypes)
        {
            _messageFactories.Add(messageType, (MessageHeader _, SerializationContext _, out int c) =>
            {
                c = 0;
                return new T();
            });
        }
    }

    public void RegisterController(Type controllerType, string appName = null)
    {
        if (!typeof(RtmpController).IsAssignableFrom(controllerType))
            throw new InvalidOperationException("controllerType must inherit from AbstractController");
        var name = appName ?? controllerType.Name.Replace("Controller", "");
        _registeredControllers.Add(name.ToLower(), controllerType);
        _rpcService.RegeisterController(controllerType);
        Builder.RegisterType(controllerType).AsSelf();
    }
    public void RegisterStream(Type streamType)
    {
        if (!typeof(NetStream).IsAssignableFrom(streamType))
            throw new InvalidOperationException("streamType must inherit from NetStream");
        _rpcService.RegeisterController(streamType);
        Builder.RegisterType(streamType).AsSelf();
    }

    internal void CleanupRpcRegistration() => _rpcService.CleanupRegistration();

    private void RegisterCommonServices(ContainerBuilder builder)
    {
        builder.Register(_ => new RecordServiceConfiguration())
            .AsSelf();
        builder.Register(c => new RecordService(c.Resolve<RecordServiceConfiguration>()))
            .AsSelf()
            .InstancePerLifetimeScope();
        builder.Register(_ => new PublisherSessionService())
            .AsSelf()
            .InstancePerLifetimeScope();
        builder.Register(_ => _rpcService)
            .AsSelf()
            .SingleInstance();
    }

    public void RegisterController<T>(string appName = null) where T : RtmpController => RegisterController(typeof(T), appName);

    public void RegisterStream<T>() where T : NetStream => RegisterStream(typeof(T));
}