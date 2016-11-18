﻿using Discord;
using Discord.Audio;
using Discord.Commands;
using System;
using System.Linq;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Net;
using System.Diagnostics;
using NAudio.Wave;
using System.Text;
using NerdyBot.Contracts;

namespace NerdyBot
{
  partial class NerdyBot : IClient
  {
    DiscordClient client;
    AudioService audio;
    Channel stdOutChannel;
    MainConfig conf;

    private object playing = new object();

    private const string MAINCFG = "cfg";

    public NerdyBot()
    {
      conf = new MainConfig( MAINCFG );
      conf.Read();

      client = new DiscordClient( x =>
       {
         x.LogLevel = LogSeverity.Info;
         x.LogHandler = LogHandler;
       } );

      client.UsingCommands( x =>
      {
        x.PrefixChar = conf.Prefix;
        x.AllowMentionPrefix = true;
      } );

      client.MessageReceived += Discord_MessageReceived;

      client.UsingAudio( x =>
      {
        x.Mode = AudioMode.Outgoing;
      } );
      this.audio = client.GetService<AudioService>();
    }

    private IEnumerable<string> preservedKeys = new string[] { "perm", "help", "stop", "leave", "join" };

    private List<ICommand> commands = new List<ICommand>();
    private void InitCommands()
    {
      try
      {
        Assembly assembly = Assembly.GetExecutingAssembly();

        foreach ( Type type in assembly.GetTypes() )
        {
          if ( type.GetInterface( "ICommand" ) != null )
          {
            ICommand command = ( ICommand )Activator.CreateInstance( type, null, null );
            command.Init( this );
            if ( !CanLoadCommand( command ) )
              throw new InvalidOperationException( "Duplicated command key or alias: " + command.Config.Key );
            this.commands.Add( command );
          }
        }
      }
      catch ( Exception ex )
      {
        throw new InvalidOperationException( "Schade", ex );
      }
    }

    private async void Discord_MessageReceived( object sender, MessageEventArgs e )
    {
      bool isCommand = false;
      if ( stdOutChannel == null && e.Server != null )
        stdOutChannel = e.Server.GetChannel( conf.ResponseChannel );

      if ( e.Message.Text.Length > 1 && e.Message.Text.StartsWith( conf.Prefix.ToString() ) )
      {
        if ( e.Server != null )
        {
          string[] args = e.Message.Text.Substring( 1 ).Split( ' ' );
          string input = args[0].ToLower();
          if ( input == "perm" )
          {
            isCommand = true;
            RestrictCommandByRole( e, args.Skip( 1 ).ToArray() );
          }
          else if ( input == "help" )
          {
            isCommand = true;
            ShowHelp( e, args.Skip( 1 ).ToArray() );
          }
          else if ( input == "stop" )
            this.StopPlaying = true;
          else
          {
            foreach ( var cmd in this.commands )
            {
              if ( input == cmd.Config.Key || cmd.Config.Aliases.Any( a => input == a ) )
              {
                isCommand = true;
                if ( CanExecute( e.User, cmd ) )
                  cmd.Execute( new CommandMessage()
                  {
                    Arguments = args.Skip( 1 ).ToArray(),
                    Text = e.Message.Text,
                    Channel = new CommandChannel()
                    {
                      Id = e.Channel.Id,
                      Mention = e.Channel.Mention,
                      Name = e.Channel.Name
                    },
                    User = new CommandUser()
                    {
                      Id = e.User.Id,
                      Name = e.User.Name,
                      FullName = e.User.ToString(),
                      Mention = e.User.Mention,
                      VoiceChannelId = e.User.VoiceChannel == null ? 0 : e.User.VoiceChannel.Id,
                      Permissions = e.User.ServerPermissions
                    }
                  } );
              }
            }
          }
        }
        else
          e.Channel.SendMessage( "Ich habe dir hier nichts zu sagen!" );
      }

      if ( isCommand )
        e.Message.Delete();
    }

    private bool CanExecute( User user, ICommand cmd )
    {
      bool ret = false;
      if ( user.ServerPermissions.Administrator )
        return true;

      switch ( cmd.Config.RestrictionType )
      {
      case Commands.Config.RestrictType.Admin:
        ret = false;
        break;
      case Commands.Config.RestrictType.None:
        ret = true;
        break;
      case Commands.Config.RestrictType.Allow:
        ret = ( cmd.Config.RestrictedRoles.Count() == 0 || user.Roles.Any( r => cmd.Config.RestrictedRoles.Any( id => r.Id == id ) ) );
        break;
      case Commands.Config.RestrictType.Deny:
        ret = ( cmd.Config.RestrictedRoles.Count() == 0 || !user.Roles.Any( r => cmd.Config.RestrictedRoles.Any( id => r.Id == id ) ) );
        break;
      default:
        throw new InvalidOperationException( "WTF?!?!" );
      }
      return ret;
    }

    private bool CanLoadCommand( ICommand command )
    {
      if ( preservedKeys.Contains( command.Config.Key ) )
        return false;
      if ( this.commands.Any( cmd => cmd.Config.Key == command.Config.Key ||
          cmd.Config.Aliases.Any( ali => ali == command.Config.Key ||
            command.Config.Aliases.Any( ali2 => ali == ali2 ) ) ) )
        return false;
      return true;
    }

    #region MainCommands
    private void RestrictCommandByRole( MessageEventArgs e, string[] args )
    {
      string info = string.Empty;
      if ( e.User.ServerPermissions.Administrator )
      {
        string option = args.First().ToLower();
        switch ( option )
        {
        case "add":
        case "rem":
          if ( args.Count() != 3 )
            info = "Äh? Ich glaube die Parameteranzahl stimmt so nicht!";
          else
          {
            var role = e.Server.Roles.FirstOrDefault( r => r.Name == args[1] );
            var cmd = this.commands.FirstOrDefault( c => c.Config.Key == args[2] );
            if ( cmd == null )
              info = "Command nicht gefunden!";
            else
            {
              if ( cmd.Config.RestrictedRoles.Contains( role.Id ) )
              {
                if ( option == "rem" )
                  cmd.Config.RestrictedRoles.Remove( role.Id );
              }
              else
              {
                if ( option == "add" )
                  cmd.Config.RestrictedRoles.Add( role.Id );
              }
              info = "Permissions updated for " + cmd.Config.Key;
            }
          }
          break;
        case "type":
          if ( args.Count() != 3 )
            info = "Äh? Ich glaube die Parameteranzahl stimmt so nicht!";
          else
          {
            var cmd = this.commands.FirstOrDefault( c => c.Config.Key == args[2] );
            switch ( args[1].ToLower() )
            {
            case "admin":
              cmd.Config.RestrictionType = Commands.Config.RestrictType.Admin;
              break;
            case "none":
              cmd.Config.RestrictionType = Commands.Config.RestrictType.None;
              break;
            case "deny":
              cmd.Config.RestrictionType = Commands.Config.RestrictType.Deny;
              break;
            case "allow":
              cmd.Config.RestrictionType = Commands.Config.RestrictType.Allow;
              break;
            default:
              SendMessage( args[1] + " ist ein invalider Parameter", new SendMessageOptions() { TargetType = TargetType.Channel, TargetId = e.Channel.Id, MessageType = MessageType.Info } );
              return;
            }
            info = "Permissions updated for " + cmd.Config.Key;
          }
          break;
        case "help":
          //TODO HELP
          break;
        default:
          info = "Invalider Parameter!";
          break;
        }
      }
      else
        info = "Du bist zu unwichtig dafür!";
      SendMessage( info, new SendMessageOptions() { TargetType = TargetType.Channel, TargetId = e.Channel.Id, MessageType = MessageType.Info } );
    }
    private void ShowHelp( MessageEventArgs e, IEnumerable<string> args )
    {
      StringBuilder sb = new StringBuilder();
      if ( args.Count() == 0 )
      {
        this.commands.ForEach( ( cmd ) =>
        {
          sb.AppendLine( cmd.QuickHelp() );
          sb.AppendLine();
          sb.AppendLine();
        } );
      }
      else
      {
        var command = this.commands.FirstOrDefault( cmd => cmd.Config.Key == args.First() || cmd.Config.Aliases.Any( ali => ali == args.First() ) );
        if ( commands != null )
          sb = new StringBuilder( command.FullHelp( this.conf.Prefix ) );
        else
          sb = new StringBuilder( "Du kennst anscheinend mehr Commands als ich!" );
      }
      SendMessage( sb.ToString(), new SendMessageOptions() { TargetType = TargetType.User, TargetId = e.User.Id, Split = true, MessageType = MessageType.Block } );
    }
    #endregion MainCommands

    private void LogHandler( object sender, LogMessageEventArgs e )
    {
      Console.WriteLine( e.Message );
    }

    public void Start()
    {
      InitCommands();
      client.ExecuteAndWait( async () =>
       {
         await client.Connect( conf.Token, TokenType.Bot );
         client.SetGame( "Not nerdy at all" );
       } );
    }

    #region IClient
    public MainConfig Config { get { return this.conf; } }

    public bool StopPlaying { get; set; }
    public void DownloadAudio( string url, string outp )
    {
      try
      {
        bool transform = false;
        string ext = Path.GetExtension( url );
        Log( "downloading " + url );
        if ( ext != string.Empty )
        {
          string tempOut = Path.Combine( Path.GetDirectoryName( outp ), "temp" + ext );
          if ( ext == ".mp3" )
            tempOut = outp;

          if ( !Directory.Exists( Path.GetDirectoryName( tempOut ) ) )
            Directory.CreateDirectory( Path.GetDirectoryName( tempOut ) );

          ( new WebClient() ).DownloadFile( url, tempOut );

          transform = ( ext != ".mp3" );
        }
        else
        {
          string tempOut = Path.Combine( Path.GetDirectoryName( outp ), "temp.%(ext)s" );
          ProcessStartInfo ytdl = new System.Diagnostics.ProcessStartInfo();
          ytdl.WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden;
          ytdl.FileName = "ext\\youtube-dl.exe";

          ytdl.Arguments = "--extract-audio --audio-quality 0 --no-playlist --output \"" + tempOut + "\" \"" + url + "\"";
          Process.Start( ytdl ).WaitForExit();
          transform = true;
        }
        if ( transform )
        {
          string tempFIle = Directory.GetFiles( Path.GetDirectoryName( outp ), "temp.*", SearchOption.TopDirectoryOnly ).First();

          ProcessStartInfo ffmpeg = new System.Diagnostics.ProcessStartInfo();
          ffmpeg.WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden;
          ffmpeg.FileName = "ext\\ffmpeg.exe";

          ffmpeg.Arguments = "-i " + tempFIle + " -f mp3 " + outp;
          Process.Start( ffmpeg ).WaitForExit();

          File.Delete( tempFIle );
        }
      }
      catch ( Exception ex )
      {
        throw new ArgumentException( ex.Message );
      }
    }
    public async void SendAudio( ulong channelId, string localPath, bool delAfterPlay = false )
    {
      Channel vChannel = this.client.Servers.First().VoiceChannels.FirstOrDefault( vc => vc.Id == channelId );
      lock ( playing )
      {
        IAudioClient vClient = audio.Join( vChannel ).Result;
        Log( "reading " + Path.GetDirectoryName( localPath ) );
        var channelCount = audio.Config.Channels; // Get the number of AudioChannels our AudioService has been configured to use.
        var OutFormat = new WaveFormat( 48000, 16, channelCount ); // Create a new Output Format, using the spec that Discord will accept, and with the number of channels that our client supports.
        using ( var MP3Reader = new Mp3FileReader( localPath ) ) // Create a new Disposable MP3FileReader, to read audio from the filePath parameter
        using ( var resampler = new MediaFoundationResampler( MP3Reader, OutFormat ) ) // Create a Disposable Resampler, which will convert the read MP3 data to PCM, using our Output Format
        {
          resampler.ResamplerQuality = 60; // Set the quality of the resampler to 60, the highest quality

          int blockSize = OutFormat.AverageBytesPerSecond / 50; // Establish the size of our AudioBuffer
          byte[] buffer = new byte[blockSize];
          int byteCount;

          Log( "start sending audio" );
          while ( ( byteCount = resampler.Read( buffer, 0, blockSize ) ) > 0 && !StopPlaying ) // Read audio into our buffer, and keep a loop open while data is present
          {
            if ( byteCount < blockSize )
            {
              // Incomplete Frame
              for ( int i = byteCount; i < blockSize; i++ )
                buffer[i] = 0;
            }
            vClient.Send( buffer, 0, blockSize ); // Send the buffer to Discord
          }
          Log( "finished sending" );
        }
        vClient.Wait();
        StopPlaying = false;
        if ( delAfterPlay )
          File.Delete( localPath );
      }
    }

    public async void SendMessage( string message, SendMessageOptions options )
    {
      switch ( options.TargetType )
      {
      case TargetType.User:
        User usr = this.client.Servers.First().GetUser( options.TargetId );
        if ( !options.Split && message.Length > 1990 )
        {
          File.WriteAllText( "raw.txt", message );
          await usr.SendFile( "raw.txt" );
          File.Delete( "raw.txt" );
        }
        else
        {
          foreach ( string msg in ChunkMessage( message ) )
            usr.SendMessage( FormatMessage( msg, options.MessageType, options.Hightlight ) );
        }
        break;
      case TargetType.Channel:
        {
          Channel ch = this.client.Servers.First().GetChannel( options.TargetId );
          if ( !options.Split && message.Length > 1990 )
          {
            File.WriteAllText( "raw.txt", message );
            await ch.SendFile( "raw.txt" );
            File.Delete( "raw.txt" );
          }
          else
          {
            foreach ( string msg in ChunkMessage( message ) )
              ch.SendMessage( FormatMessage( msg, options.MessageType, options.Hightlight ) );
          }
        }
        break;
      case TargetType.Default:
      default:
        {
          Channel ch = this.client.GetChannel( conf.ResponseChannel );
          if ( !options.Split && message.Length > 1990 )
          {
            File.WriteAllText( "raw.txt", message );
            await ch.SendFile( "raw.txt" );
            File.Delete( "raw.txt" );
          }
          else
          {
            foreach ( string msg in ChunkMessage( message ) )
              ch.SendMessage( FormatMessage( msg, options.MessageType, options.Hightlight ) );
          }
        }
        break;
      }
    }
    private string FormatMessage( string message, MessageType format, string highlight )
    {
      string formatedMessage = string.Empty;
      switch ( format )
      {
      case MessageType.Block:
        formatedMessage = "```" + highlight + Environment.NewLine + message + "```";
        break;
      case MessageType.Info:
        formatedMessage = "`" + message + "`";
        break;
      case MessageType.Normal:
      default:
        formatedMessage = message;
        break;
      }
      return formatedMessage;
    }

    public void Log( string text )
    {
      this.client.Log.Log( LogSeverity.Info, "", text );
    }
    #endregion

    private readonly int chunkSize = 1990;
    private IEnumerable<string> ChunkMessage( string str )
    {
      if ( str.Length > chunkSize )
        return Enumerable.Range( 0, str.Length / chunkSize )
          .Select( i => str.Substring( i * chunkSize, chunkSize ) );
      return new string[] { str };
    }
  }
}