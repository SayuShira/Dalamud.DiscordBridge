using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.DiscordBridge.Model;
using Dalamud.DiscordBridge.XivApi;
using Dalamud.Game.Chat;
using Dalamud.Game.Chat.SeStringHandling;
using Dalamud.Game.Chat.SeStringHandling.Payloads;
using Dalamud.Plugin;

namespace Dalamud.DiscordBridge
{
    public class DiscordMessageQueue
    {
        private volatile bool runQueue = true;

        private readonly Plugin plugin;
        private readonly Thread runnerThread;

        private readonly ConcurrentQueue<QueuedXivEvent> eventQueue = new ConcurrentQueue<QueuedXivEvent>();

        private readonly Dictionary<ClientLanguage, Regex[]> retainerSaleRegexes = new Dictionary<ClientLanguage, Regex[]>() { {
                ClientLanguage.Japanese, new Regex[] {
                    new Regex(@"^(?:.+)マーケットに(?<origValue>[\d,.]+)ギルで出品した(?<item>.*)×(?<count>[\d,.]+)が売れ、(?<value>[\d,.]+)ギルを入手しました。$", RegexOptions.Compiled),
                    new Regex(@"^(?:.+)マーケットに(?<origValue>[\d,.]+)ギルで出品した(?<item>.*)が売れ、(?<value>[\d,.]+)ギルを入手しました。$", RegexOptions.Compiled) }
            }, {
                ClientLanguage.English, new Regex[] {
                    new Regex(@"^(?<item>.+) you put up for sale in the (?:.+) markets (?:have|has) sold for (?<value>[\d,.]+) gil \(after fees\)\.$", RegexOptions.Compiled)
                }
            }, {
                ClientLanguage.German, new Regex[] {
                    new Regex(@"^Dein Gehilfe hat (?<item>.+) auf dem Markt von (?:.+) für (?<value>[\d,.]+) Gil verkauft\.$", RegexOptions.Compiled),
                    new Regex(@"^Dein Gehilfe hat (?<item>.+) auf dem Markt von (?:.+) verkauft und (?<value>[\d,.]+) Gil erhalten\.$", RegexOptions.Compiled)
                }
            }, {
                ClientLanguage.French, new Regex[] {
                    new Regex(@"^Un servant a vendu (?<item>.+) pour (?<value>[\d,.]+) gil à (?:.+)\.$", RegexOptions.Compiled)
                }
            }
        };

        public DiscordMessageQueue(Plugin plugin)
        {
            this.plugin = plugin;
            this.runnerThread = new Thread(RunMessageQueue);
        }

        public void Start()
        {
            this.runQueue = true;
            this.runnerThread.Start();
        }

        public void Stop()
        {
            this.runQueue = false;

            if(this.runnerThread.IsAlive)
                this.runnerThread.Join();
        }

        public void Enqueue(QueuedXivEvent @event) => this.eventQueue.Enqueue(@event);

        private async void RunMessageQueue()
        {
            while (this.runQueue)
            {
                if (this.eventQueue.TryDequeue(out var resultEvent))
                {
                    try
                    {

                        if (resultEvent is QueuedRetainerItemSaleEvent retainerSaleEvent)
                        {
                            try
                            {

                                //foreach (var regex in retainerSaleRegexes[this.plugin.Interface.ClientState.ClientLanguage])
                                {
                                    //var matchInfo = regex.Match(retainerSaleEvent.Message.TextValue);

                                    var itemLink =
                                    retainerSaleEvent.Message.Payloads.First(x => x.Type == PayloadType.Item) as ItemPayload;

                                    var avatarUrl = Constant.LogoLink;

                                    if (itemLink == null)
                                    {
                                        PluginLog.Error("itemLink was null. Msg: {0}", BitConverter.ToString(retainerSaleEvent.Message.Encode()));
                                        break;
                                    }
                                    else
                                    {

                                        // XIVAPI wants these padded with 0s in the front if under 6 digits
                                        // at least if Titanium Ore testing is to be believed. 
                                        var iconFolder = $"{itemLink.Item.Icon / 1000 * 1000}".PadLeft(6,'0');
                                        var iconFile = $"{itemLink.Item.Icon}".PadLeft(6, '0');

                                        avatarUrl = $"https://xivapi.com" + $"/i/{iconFolder}/{iconFile}.png";
                                        /* 
                                        // we don't need this anymore because the above should work
                                        // but it doesn't hurt to have it commented out as a fallback for the future
                                        try
                                        {
                                            ItemResult res = XivApiClient.GetItem(itemLink.Item.RowId).GetAwaiter().GetResult();
                                            avatarUrl = $"https://xivapi.com{res.Icon}";
                                        }
                                        catch (Exception ex)
                                        {
                                            PluginLog.Error(ex, "Cannot fetch XIVAPI item search.");
                                        }
                                        */
                                    }

                                    //var valueInfo = matchInfo.Groups["value"];
                                    // not sure if using a culture here would work correctly, so just strip symbols instead
                                    //if (!valueInfo.Success || !int.TryParse(valueInfo.Value.Replace(",", "").Replace(".", ""), out var itemValue))
                                    //    continue;

                                    //SendItemSaleEvent(uint itemId, int amount, bool isHq, string message, XivChatType chatType)

                                    await this.plugin.Discord.SendItemSaleEvent(itemLink.Item.Name, avatarUrl, itemLink.Item.RowId, retainerSaleEvent.Message.TextValue, retainerSaleEvent.ChatType);
                                }
                            }
                            catch (Exception e)
                            {
                                PluginLog.Error(e, "Could not send discord message.");
                            }
                        }

                        if (resultEvent is QueuedChatEvent chatEvent)
                        {
                            var senderName = (chatEvent.ChatType == XivChatType.TellOutgoing || chatEvent.ChatType == XivChatType.Echo)
                                ? this.plugin.Interface.ClientState.LocalPlayer.Name
                                : chatEvent.Sender.ToString();
                            var senderWorld = string.Empty;

                            try
                            {
                                if (this.plugin.Interface.ClientState.LocalPlayer != null)
                                {
                                    var playerLink = chatEvent.Sender.Payloads.FirstOrDefault(x => x.Type == PayloadType.Player) as PlayerPayload;

                                    if (playerLink == null)
                                    {
                                        // chat messages from the local player do not include a player link, and are just the raw name
                                        // but we should still track other instances to know if this is ever an issue otherwise

                                        // Special case 2 - When the local player talks in party/alliance, the name comes through as raw text,
                                        // but prefixed by their position number in the party (which for local player may always be 1)
                                        if (chatEvent.Sender.TextValue.EndsWith(this.plugin.Interface.ClientState
                                            .LocalPlayer.Name))
                                        {
                                            senderName = this.plugin.Interface.ClientState.LocalPlayer.Name;
                                        }
                                        else
                                        {
                                            // Franz is really tired of getting playerlink is null when there shouldn't be a player link for certain things
                                            switch (chatEvent.ChatType)
                                            {
                                                case XivChatType.Debug:
                                                    break;
                                                case XivChatType.Urgent:
                                                    break;
                                                case XivChatType.Notice:
                                                    break;
                                                case XivChatType.TellOutgoing:
                                                    senderName = this.plugin.Interface.ClientState.LocalPlayer.Name;
                                                    // senderWorld = this.plugin.Interface.ClientState.LocalPlayer.HomeWorld.GameData.Name;
                                                    break;
                                                case XivChatType.Echo:
                                                    senderName = this.plugin.Interface.ClientState.LocalPlayer.Name;
                                                    // senderWorld = this.plugin.Interface.ClientState.LocalPlayer.HomeWorld.GameData.Name;
                                                    break;
                                                case XivChatType.SystemMessage:
                                                    break;
                                                case XivChatType.SystemError:
                                                    break;
                                                case XivChatType.GatheringSystemMessage:
                                                    break;
                                                case XivChatType.ErrorMessage:
                                                    break;
                                                case (XivChatType)61: // retainerspeak
                                                    break;
                                                case (XivChatType)68: // battle NPCs
                                                    break;
                                                default:
                                                    if ((int)chatEvent.ChatType > 107) // don't handle anything past CWLS8 for now
                                                        break;
                                                    PluginLog.Error("playerLink was null. Sender: {0}",
                                                        BitConverter.ToString(chatEvent.Sender.Encode()));
                                                    senderName = chatEvent.Sender.TextValue;
                                                    PluginLog.Information($"Type: {chatEvent.ChatType} Sender: {chatEvent.Sender.TextValue} "
                                                        + $"Message: {chatEvent.Message.TextValue}");
                                                    break;
                                            }
                                            

                                            senderName = chatEvent.ChatType == XivChatType.TellOutgoing
                                                ? this.plugin.Interface.ClientState.LocalPlayer.Name
                                                : chatEvent.Sender.TextValue;
                                        }

                                        senderWorld = this.plugin.Interface.ClientState.LocalPlayer.HomeWorld.GameData
                                            .Name;
                                        // PluginLog.Information($"Playerlink is null: {senderWorld}");
                                    }
                                    else
                                    {
                                        senderName = chatEvent.ChatType == XivChatType.TellOutgoing
                                            ? this.plugin.Interface.ClientState.LocalPlayer.Name
                                            : playerLink.PlayerName;
                                        senderWorld = chatEvent.ChatType == XivChatType.TellOutgoing
                                            ? this.plugin.Interface.ClientState.LocalPlayer.HomeWorld.GameData.Name
                                            : playerLink.World.Name;
                                        // PluginLog.Information($"Playerlink was not null: {senderWorld}");
                                    }
                                }
                                else
                                {
                                    senderName = string.Empty;
                                    senderWorld = string.Empty;
                                }
                            }
                            catch(Exception ex)
                            {
                                PluginLog.Error(ex, "Could not deduce player name.");
                            }
                            

                            try
                            {
                                await this.plugin.Discord.SendChatEvent(chatEvent.Message.TextValue, senderName, senderWorld, chatEvent.ChatType);
                            }
                            catch (Exception e)
                            {
                                PluginLog.Error(e, "Could not send discord message.");
                            }
                        }

                        if (resultEvent is QueuedContentFinderEvent cfEvent)
                            try
                            {
                                await this.plugin.Discord.SendContentFinderEvent(cfEvent);
                            }
                            catch (Exception e)
                            {
                                PluginLog.Error(e, "Could not send discord message.");
                            }

                        
                    }
                    catch (Exception e)
                    {
                        PluginLog.Error(e, "Could not process event.");
                    }
                }

                Thread.Yield();
            }
        }
    }
}
