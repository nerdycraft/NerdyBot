﻿using System;
using System.Linq;
using System.Text;
using System.Reflection;
using System.Threading.Tasks;

using Discord.Commands;
using Discord.WebSocket;

using NerdyBot.Services;
using NerdyBot.Models;

namespace NerdyBot.Modules
{
  public class NerdyBotBase : ModuleBase
  {
    public BotConfig BotConfig { get; set; }
    public AudioService AudioService { get; set; }
    public MessageService MessageService { get; set; }
    public DiscordSocketClient Client { get; set; }

    [Command( "purge", RunMode = RunMode.Async ), Alias( "t" )]
    [RequireUserPermission( Discord.GuildPermission.Administrator )]
    [RequireBotPermission( Discord.ChannelPermission.ManageMessages )]
    public async Task Purge( int count )
    {
      var enumerator = Context.Channel.GetMessagesAsync( count ).GetEnumerator();
      while ( await enumerator.MoveNext( new System.Threading.CancellationToken() ) )
        await Context.Channel.DeleteMessagesAsync( enumerator.Current );
    }

    [Command( "stop" )]
    public void StopPlaying()
    {
      AudioService.StopPlaying( Context.Guild.Id );
    }

    [Command( "leave" )]
    public async Task LeaveChannel()
    {
      await AudioService.LeaveChannel( Context.Guild.Id );
    }

    [Command( "join" )]
    public async Task JoinChannel()
    {
      await AudioService.JoinChannel( new AudioContext() { GuildId = Context.Guild.Id, UserId = Context.User.Id } );
    }

    [Command( "exit" )]
    public async Task Exit()
    {
      if ( Context.User.Id == BotConfig.AdminUserId )
      {
        foreach ( var guild in Client.Guilds )
        {
          AudioService.StopPlaying( guild.Id );
          await AudioService.LeaveChannel( guild.Id );
          await guild.DefaultChannel.SendMessageAsync( "Sorry guys i got to go, mom is calling :rolling_eyes:" );
        }
        await Context.Message.DeleteAsync();
        BotConfig.IsStopped = true;
        Client.StopAsync();
      }
    }
    
    [Command( "help" )]
    public void ShowHelp()
    {
      StringBuilder sb = new StringBuilder();
      Assembly assembly = Assembly.GetExecutingAssembly();

      foreach ( Type type in assembly.GetTypes().Where( type => type.BaseType == typeof( ModuleBase ) ) )
      {
        MethodInfo help;
        if ( ( help = type.GetMethod( "QuickHelp" ) ) != null )
        {
           sb.AppendLine( (string)help.Invoke( null, null ) );
           sb.AppendLine();
           sb.AppendLine();
         }
       }
       MessageService.SendMessageToCurrentUser( Context, sb.ToString(), MessageType.Info, true );
     }
  }
}
