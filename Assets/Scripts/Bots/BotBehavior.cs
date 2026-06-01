using StumbleClone.Core;

namespace StumbleClone.Bots
{
    /// Strategy assigned to a BotController by the spawner based on LevelMode.
    /// Not a MonoBehaviour — instantiated as a plain object and ticked by the bot.
    public abstract class BotBehavior
    {
        public abstract void Tick(BotController bot);

        public virtual void OnAttach(BotController bot) { }
        public virtual void OnDetach(BotController bot) { }
    }
}
