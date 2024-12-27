using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading.Tasks;
using Dalamud.DiscordBridge.Model;
using Dalamud.Game.Text;
using Dalamud.Plugin.Services;
using Discord;
using Discord.Webhook;
using Discord.WebSocket;
using Lumina.Text.ReadOnly;
using NetStone;
using NetStone.Model.Parseables.Character;
using NetStone.Search.Character;
namespace Dalamud.DiscordBridge
{
public class ChatHandler
{
   static readonly IPluginLog Logger = Service.Logger;
   public void HandleMessage(string channel, string message)
   {
      Logger.Error($"[ChatHandler] Channel: {channel}, Message: {message}");
   }
}
}