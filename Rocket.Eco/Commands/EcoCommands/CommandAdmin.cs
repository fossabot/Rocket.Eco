﻿using System;
using Eco.Core.Plugins.Interfaces;
using Eco.Gameplay.Players;
using Rocket.API.Commands;
using Rocket.API.Logging;
using Rocket.API.Player;
using Rocket.Core.Commands;
using Rocket.Core.Logging;
using Rocket.Core.User;
using Rocket.Eco.API;
using Rocket.Eco.Player;

namespace Rocket.Eco.Commands.EcoCommands
{
    /// <inheritdoc />
    /// <summary>
    ///     A command to add a player to the admin list.
    /// </summary>
    public sealed class CommandAdmin : ICommand
    {
        /* why is this gone? D:
        /// <inheritdoc />
        public string Permission => "Rocket.Admin";
        */

        /// <inheritdoc />
        public bool SupportsUser(Type user) => true;

        /// <inheritdoc />
        public string Name => "Admin";

        /// <inheritdoc />
        public string[] Aliases => new[] {"AddAdmin", "SetAdmin"};

        /// <inheritdoc />
        public string Summary => "Makes a player an administrator. (THIS IS DANGEROUS TO GRANT!)";

        /// <inheritdoc />
        public string Description => null;

        /// <inheritdoc />
        public string Syntax => "[[n]ame / id]";

        /// <inheritdoc />
        public IChildCommand[] ChildCommands => new IChildCommand[0];

        /// <inheritdoc />
        public void Execute(ICommandContext context)
        {
            if (context.Parameters.Length == 0)
                throw new CommandWrongUsageException();

            IPlayerManager playerManager = context.Container.Resolve<IPlayerManager>("eco");

            if (!playerManager.TryGetOnlinePlayer(context.Parameters[0], out IPlayer player))
            {
                context.User.SendMessage("The requested user needs to be online.");
                return;
            }

            EcoPlayer ecoPlayer = (EcoPlayer) player;

            if (ecoPlayer.UserIdType == UserIdType.Both)
                UserManager.Config.Admins.Add(ecoPlayer.InternalEcoUser.SteamId);

            UserManager.Config.Admins.Add(player.Id);
            UserManager.Obj.SaveConfig();

            context.Container.Resolve<ILogger>().LogInformation($"{context.User.Name} has granted {player.Name} administrator permissions.");

            context.User.SendMessage("The requested user has been made an administrator.");
            ((EcoPlayer) player).User.SendMessage("You have been granted administrator permissions.");
        }
    }
}