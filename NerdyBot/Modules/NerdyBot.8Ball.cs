﻿using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Discord.Commands;

using NerdyBot.Services;
using NerdyBot.Models;

namespace NerdyBot.Commands
{
  [Group( "8ball" ), Alias( "8b" )]
  public class Ball8 : ModuleBase
  {
    public MessageService MessageService { get; set; }
    public DatabaseService DatabaseService { get; set; }

    public Ball8( DatabaseService databaseService )
    {
      databaseService.Database.CreateTable<Ball8Answer>();
    }

    [Command( "add" )]
    public async Task Add( params string[] content )
    {
      foreach ( var entry in content )
        DatabaseService.Database.Insert( new Ball8Answer() { Answer = entry } );
    }

    [Command( "help" )]
    public async Task Help()
    {
      MessageService.SendMessage( Context, FullHelp(),
        new SendMessageOptions() { TargetType = TargetType.User, TargetId = Context.User.Id, MessageType = MessageType.Block } );
    }

    [Command()]
    public async Task Execute( string question )
    {
      var answers = DatabaseService.Database.Table<Ball8Answer>().ToList();
      int idx = ( new Random() ).Next( 0, answers.Count() );
      MessageService.SendMessage( Context, $"{Context.User.Mention} asked: '{question}'{Environment.NewLine}{Environment.NewLine}" + answers[idx],
        new SendMessageOptions() { TargetType = TargetType.Channel, TargetId = Context.Channel.Id } );
    }

    public static string QuickHelp()
    {
      StringBuilder sb = new StringBuilder();
      sb.AppendLine( "======== 8Ball ========" );
      sb.AppendLine();
      sb.AppendLine( "Magic 8Ball beantwortet dir jede GESCHLOSSENE Frage, die du an ihn richtest" );
      sb.AppendLine( "Key: 8ball" );
      return sb.ToString();
    }
    public static string FullHelp()
    {
      StringBuilder sb = new StringBuilder();
      sb.Append( QuickHelp() );
      sb.AppendLine( "Aliase: 8b" );
      sb.AppendLine();
      sb.AppendLine( "Beispiel: 8ball [FRAGE]" );
      return sb.ToString();
    }
  }
}