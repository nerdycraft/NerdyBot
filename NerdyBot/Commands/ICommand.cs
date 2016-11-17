﻿using Discord;
using NerdyBot.Commands.Config;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace NerdyBot.Commands
{
  interface ICommand
  {
    BaseCommandConfig Config { get; }
    void Init();
    Task Execute( MessageEventArgs msg, string[] args, IClient client );
    string QuickHelp();
  }
}
