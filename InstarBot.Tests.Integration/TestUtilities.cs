using Discord;
using Discord.Interactions;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Moq;
using Moq.Protected;
using PaxAndromeda.Instar;
using Xunit;

namespace InstarBot.Tests.Integration;

public static class TestUtilities
{
    public static IConfiguration GetTestConfiguration()
    {
        IConfiguration config = new ConfigurationBuilder()
#if DEBUG
            .AddJsonFile("Config/Instar.test.debug.conf.json")
#else
            .AddJsonFile("Config/Instar.test.conf.json")
#endif
            .Build();

        return config;
    }
    
    /// <summary>
    /// Provides an method for verifying messages with an ambiguous Mock type.
    /// </summary>
    /// <param name="mockObject">The mock of the command.</param>
    /// <param name="message">The message to search for.</param>
    /// <param name="ephemeral">A flag indicating whether the message should be ephemeral.</param>
    public static void VerifyMessage(object mockObject, string message, bool ephemeral = false)
    {
        // A few checks first
        var mockObjectType = mockObject.GetType();
        Assert.Equal(nameof(Mock), mockObjectType.Name[..mockObjectType.Name.LastIndexOf('`')]);
        Assert.Single(mockObjectType.GenericTypeArguments);
        var commandType = mockObjectType.GenericTypeArguments[0];
        
        var genericVerifyMessage = typeof(TestUtilities)
            .GetMethods()
            .Where(n => n.Name == nameof(VerifyMessage))
            .Select(m => new
            {
                Method = m,
                Params = m.GetParameters(),
                Args = m.GetGenericArguments()
            })
            .Where(x => x.Args.Length == 1)
            .Select(x => x.Method)
            .First();

        var specificMethod = genericVerifyMessage.MakeGenericMethod(commandType);
        specificMethod.Invoke(null, new[] { mockObject, message, ephemeral });
    }
    
    /// <summary>
    /// Verifies that the command responded to the user with the correct <paramref name="message"/>.
    /// </summary>
    /// <param name="command">The mock of the command.</param>
    /// <param name="message">The message to check for.</param>
    /// <param name="ephemeral">A flag indicating whether the message should be ephemeral.</param>
    /// <typeparam name="T">The type of command.  Must implement <see cref="InteractionModuleBase&lt;T&gt;"/>.</typeparam>
    public static void VerifyMessage<T>(Mock<T> command, string message, bool ephemeral = false)
        where T : InteractionModuleBase<SocketInteractionContext>
    {
        command.Protected().Verify(
            "RespondAsync", Times.Once(),
            message, ItExpr.IsAny<Embed[]>(),
            false, ephemeral, ItExpr.IsAny<AllowedMentions>(), ItExpr.IsAny<RequestOptions>(),
            ItExpr.IsAny<MessageComponent>(), ItExpr.IsAny<Embed>());
    }

    public static Mock<T> SetupCommandMock<T>(CommandMockContext context = null!)
        where T : InteractionModuleBase<SocketInteractionContext>
    {
        // Quick check:  Do we have a constructor that takes IConfiguration?
        var iConfigCtor = typeof(T).GetConstructors()
            .Any(n => n.GetParameters().Any(info => info.ParameterType == typeof(IConfiguration)));
        
        var commandMock = iConfigCtor ? new Mock<T>(GetTestConfiguration()) : new Mock<T>();
        ConfigureCommandMock(commandMock, context);
        return commandMock;
    }

    private static void ConfigureCommandMock<T>(Mock<T> mock, CommandMockContext? context)
        where T : InteractionModuleBase<SocketInteractionContext>
    {
        context ??= new CommandMockContext();
        
        mock.Protected().Setup<IGuildUser>("GetUser").Returns(SetupUserMock<IGuildUser>(context).Object);
        mock.Protected().Setup<IGuildChannel>("GetChannel").Returns(SetupChannelMock<ITextChannel>(context).Object);
        // Note: The following line must occur after the mocking of GetChannel.
        mock.Protected().Setup<IInstarGuild>("GetGuild").Returns(SetupGuildMock(context).Object);
        
        mock.Protected().Setup<Task>("RespondAsync", ItExpr.IsNull<string>(), ItExpr.IsNull<Embed[]>(), It.IsAny<bool>(),
                It.IsAny<bool>(), ItExpr.IsNull<AllowedMentions>(), ItExpr.IsNull<RequestOptions>(), ItExpr.IsNull<MessageComponent>(),
                ItExpr.IsNull<Embed>())
            .Returns(Task.CompletedTask);
    }

    private static Mock<IInstarGuild> SetupGuildMock(CommandMockContext? context)
    {
        context.Should().NotBeNull();
        
        var guildMock = new Mock<IInstarGuild>();
        guildMock.Setup(n => n.Id).Returns(context!.GuildID);
        guildMock.Setup(n => n.GetTextChannel(It.IsAny<ulong>()))
            .Returns(context.TextChannelMock.Object);

        return guildMock;
    }
    
    public static Mock<T> SetupUserMock<T>(ulong userId)
        where T : class, IUser
    {
        var userMock = new Mock<T>();
        userMock.Setup(n => n.Id).Returns(userId);

        return userMock;
    }

    private static Mock<T> SetupUserMock<T>(CommandMockContext? context)
        where T : class, IUser
    {
        var userMock = SetupUserMock<T>(context!.UserID);

        if (typeof(T) == typeof(IGuildUser))
            userMock.As<IGuildUser>().Setup(n => n.RoleIds).Returns(context.UserRoles);

        return userMock;
    }

    public static Mock<T> SetupChannelMock<T>(ulong channelId)
        where T : class, IChannel
    {
        var channelMock = new Mock<T>();
        channelMock.Setup(n => n.Id).Returns(channelId);

        return channelMock;
    }

    private static Mock<T> SetupChannelMock<T>(CommandMockContext? context)
        where T : class, IChannel
    {
        var channelMock = SetupChannelMock<T>(context!.ChannelID);

        if (typeof(T) != typeof(ITextChannel))
            return channelMock;

        channelMock.As<ITextChannel>().Setup(n => n.SendMessageAsync(It.IsAny<string>(), It.IsAny<bool>(),
                It.IsAny<Embed>(),
                It.IsAny<RequestOptions>(),
                It.IsAny<AllowedMentions>(), It.IsAny<MessageReference>(), It.IsAny<MessageComponent>(),
                It.IsAny<ISticker[]>(),
                It.IsAny<Embed[]>(), It.IsAny<MessageFlags>()))
            .Callback((string _, bool _, Embed embed, RequestOptions _, AllowedMentions _,
                MessageReference _, MessageComponent _, ISticker[] _, Embed[] _,
                MessageFlags _) =>
            {
                context.EmbedCallback(embed);
            })
            .Returns(Task.FromResult(new Mock<IUserMessage>().Object));

        context.TextChannelMock = channelMock.As<ITextChannel>();
        
        return channelMock;
    }
}