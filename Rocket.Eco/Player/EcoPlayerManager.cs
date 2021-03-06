﻿using System;
using System.Collections.Generic;
using System.Linq;
using Eco.Core.Plugins.Interfaces;
using Eco.Gameplay.Players;
using Eco.Gameplay.Systems.Chat;
using Eco.Shared.Utils;
using Rocket.API;
using Rocket.API.Commands;
using Rocket.API.DependencyInjection;
using Rocket.API.Eventing;
using Rocket.API.Player;
using Rocket.API.User;
using Rocket.Core.Player;
using Rocket.Core.User.Events;
using Rocket.Eco.API;
using Rocket.Eco.Extensions;
using Color = Rocket.API.Drawing.Color;

namespace Rocket.Eco.Player
{
    /// <inheritdoc cref="IPlayerManager" />
    public sealed class EcoPlayerManager : IPlayerManager
    {
        private readonly IDependencyContainer container;

        //TODO: Migrate to a thread-safe collection.
        internal readonly List<EcoPlayer> InternalPlayersList = new List<EcoPlayer>();

        /// <inheritdoc />
        public EcoPlayerManager(IDependencyContainer container)
        {
            this.container = container;

            foreach (User user in UserManager.Users)
                InternalPlayersList.Add(new EcoPlayer(user, this, container));
        }

        /// <inheritdoc />
        public string ServiceName => GetType().Name;

        /// <inheritdoc />
        public IEnumerable<IPlayer> OnlinePlayers => InternalPlayersList.Where(x => x.IsOnline);

        /// <inheritdoc />
        public void Broadcast(IUser sender, string message, Color? color = null, params object[] arguments) => Broadcast(sender, OnlineUsers, message, color, arguments);
        
        /// <inheritdoc />
        public IUserInfo GetUser(string id)
        {
            if (TryGetOnlinePlayerById(id, out IPlayer p))
                return p.GetUser();

            p = new EcoPlayer(id, this, container);
            InternalPlayersList.Add((EcoPlayer) p);

            return p.GetUser();
        }

        /// <inheritdoc />
        public IEnumerable<IUser> OnlineUsers => InternalPlayersList.Select(x => x.User);

        /// <inheritdoc />
        public IPlayer GetOnlinePlayer(string nameOrId)
        {
            IEnumerable<EcoPlayer> players = OnlinePlayers.Cast<EcoPlayer>();

            return players.FirstOrDefault(x => x.Id.Equals(nameOrId))
                ?? players.FirstOrDefault(x => x.Name.Equals(nameOrId, StringComparison.InvariantCultureIgnoreCase))
                ?? players.FirstOrDefault(x => x.Name.ComparerContains(nameOrId))
                ?? throw new EcoPlayerNotFoundException(nameOrId);
        }

        /// <inheritdoc />
        public IPlayer GetOnlinePlayerByName(string name)
        {
            IEnumerable<EcoPlayer> players = OnlinePlayers.Cast<EcoPlayer>();

            return players.FirstOrDefault(x => x.Name.Equals(name, StringComparison.InvariantCultureIgnoreCase))
                ?? players.FirstOrDefault(x => x.Name.ComparerContains(name))
                ?? throw new EcoPlayerNotFoundException(name);
        }

        /// <inheritdoc />
        public IPlayer GetOnlinePlayerById(string id)
        {
            IEnumerable<EcoPlayer> players = OnlinePlayers.Cast<EcoPlayer>();

            return players.FirstOrDefault(x => x.Id.Equals(id))
                ?? throw new EcoPlayerNotFoundException(id);
        }

        /// <inheritdoc />
        public bool TryGetOnlinePlayer(string nameOrId, out IPlayer output)
        {
            IEnumerable<EcoPlayer> players = OnlinePlayers.Cast<EcoPlayer>();

            EcoPlayer player = players.FirstOrDefault(x => x.Id.Equals(nameOrId))
                ?? players.FirstOrDefault(x => x.Name.Equals(nameOrId, StringComparison.InvariantCultureIgnoreCase))
                ?? players.FirstOrDefault(x => x.Name.ComparerContains(nameOrId));

            output = player;

            return player != null;
        }

        /// <inheritdoc />
        public bool TryGetOnlinePlayerById(string id, out IPlayer output)
        {
            IEnumerable<EcoPlayer> players = OnlinePlayers.Cast<EcoPlayer>();

            EcoPlayer player = players.FirstOrDefault(x => x.Id.Equals(id));

            output = player;

            return player != null;
        }

        /// <inheritdoc />
        public bool TryGetOnlinePlayerByName(string name, out IPlayer output)
        {
            IEnumerable<EcoPlayer> players = OnlinePlayers.Cast<EcoPlayer>();

            EcoPlayer player = players.FirstOrDefault(x => x.Name.Equals(name, StringComparison.InvariantCultureIgnoreCase))
                ?? players.FirstOrDefault(x => x.Name.ComparerContains(name));

            output = player;

            return player != null;
        }

        /// <inheritdoc />
        public IPlayer GetPlayer(string id)
        {
            if (TryGetOnlinePlayerById(id, out IPlayer p)) return p;

            p = new EcoPlayer(id, this, container);
            InternalPlayersList.Add((EcoPlayer) p);

            return p;
        }

        /// <inheritdoc />
        public bool Kick(IUser user, IUser kickedBy = null, string reason = null)
        {
            if (!(user is EcoPlayerUser ecoUser))
                throw new ArgumentException("Must be of type `EcoUser`", nameof(user));

            if (!ecoUser.IsOnline)
                throw new InvalidOperationException("You cannot kick an offline player.");

            UserKickEvent e = new UserKickEvent(ecoUser, ecoUser, reason);
            container.Resolve<IEventBus>().Emit(container.Resolve<IHost>(), e);

            if (e.IsCancelled)
                return false;

            ecoUser.Player.InternalEcoUser.Client.Disconnect("You have been kicked.", reason ?? string.Empty);

            return true;
        }

        /// <inheritdoc />
        public bool Ban(IUserInfo player, IUser caller, string reason, TimeSpan? timeSpan = null)
        {
            if (player == null)
                throw new ArgumentNullException(nameof(player));

            if (string.IsNullOrWhiteSpace(player.Id))
                throw new ArgumentException("The argument has invalid members.", nameof(player));

            if (reason == null)
                reason = string.Empty;

            UserBanEvent e = new UserBanEvent(player, caller, reason, null);
            container.Resolve<IEventBus>().Emit(container.Resolve<IHost>(), e);

            if (e.IsCancelled)
                return false;

            if (player is EcoPlayerUser ecoUser)
            {
                bool bothSucceed = false;

                if (ecoUser.Player.UserIdType == UserIdType.Both)
                    bothSucceed = AddBanBlacklist(ecoUser.Player.InternalEcoUser.SteamId);

                if (!AddBanBlacklist(ecoUser.Id) && !bothSucceed)
                    return false;

                UserManager.Obj.SaveConfig();

                if (ecoUser.IsOnline)
                    ecoUser.Player.InternalEcoUser.Client.Disconnect("You have been banned.", reason);
            }
            else
            {
                if (!AddBanBlacklist(player.Id))
                    return false;

                UserManager.Obj.SaveConfig();
            }

            return true;
        }

        /// <inheritdoc />
        public bool Unban(IUserInfo user, IUser unbannedBy = null)
        {
            switch (user)
            {
                case null:
                    throw new ArgumentNullException(nameof(user));
                case EcoPlayerUser ecoUser when ecoUser.Player.UserIdType == UserIdType.Both:
                    RemoveBanBlacklist(ecoUser.Player.InternalEcoUser.SteamId);
                    break;
            }

            return RemoveBanBlacklist(user.Id);
        }

        /// <inheritdoc />
        public void SendMessage(IUser sender, IUser receiver, string message, Color? color = null, params object[] arguments)
        {
            if (!(receiver is EcoPlayerUser ecoUser))
            {
                if (!(receiver is IConsole console))
                    throw new ArgumentException("Must be of type `EcoUser`.", nameof(receiver));

                console.WriteLine(string.IsNullOrWhiteSpace(sender?.Name) ? message : $"[{sender.Name}] {message}", arguments);
                return;
            }

            if (!ecoUser.IsOnline)
                throw new ArgumentException("Must be online.", nameof(receiver));

            string formattedMessage = string.Format(string.IsNullOrWhiteSpace(sender?.Name) ? message : $"[{sender.Name}] {message}", arguments);

            ChatManager.ServerMessageToPlayerAlreadyLocalized(formattedMessage, ecoUser.Player.InternalEcoUser);
        }

        /// <inheritdoc />
        public void Broadcast(IUser sender, IEnumerable<IUser> receivers, string message, Color? color = null, params object[] arguments)
        {
            List<EcoPlayerUser> users = new List<EcoPlayerUser>();

            foreach (IUser user in receivers)
            {
                if (!(user is EcoPlayerUser ecoUser))
                    throw new ArgumentException("Every enumeration must be of type `EcoUser`.", nameof(receivers));

                if (!ecoUser.IsOnline)
                    throw new ArgumentException("Every enumeration must be online.", nameof(receivers));

                users.Add(ecoUser);
            }

            string formattedMessage = string.Format(string.IsNullOrWhiteSpace(sender?.Name) ? message : $"[{sender.Name}] {message}", arguments);

            foreach (EcoPlayerUser ecoUser in users) ChatManager.ServerMessageToPlayerAlreadyLocalized(formattedMessage, ecoUser.Player.InternalEcoUser);
        }

        private static bool AddBanBlacklist(string user) => !string.IsNullOrWhiteSpace(user) && UserManager.Config.BlackList.AddUnique(user);
        private static bool RemoveBanBlacklist(string user) => !string.IsNullOrWhiteSpace(user) && UserManager.Config.BlackList.Remove(user);
    }

    /// <inheritdoc />
    public sealed class EcoPlayerNotFoundException : PlayerNotFoundException
    {
        /// <inheritdoc />
        public EcoPlayerNotFoundException(string nameOrId) : base(nameOrId) { }
    }
}