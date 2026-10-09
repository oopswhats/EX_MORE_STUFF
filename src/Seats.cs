using System.Text.RegularExpressions;

namespace ExMoreStuff
{
    // The free-code checker's answers. A costume slot or stage code is free unless the game has it, a mod on GameBanana
    // holds it (as last read: SharedCodes.HeldBy), or it isn't free (Catalog.NotFree). Each answer but "free" is a
    // warning: using the code anyway is the player's call.
    static class Seats
    {
        public enum Kind { Free, Game, Held, NotFree, Invalid }

        public sealed class Answer
        {
            public Kind Kind;
            public string Text;
            public GameBanana.Mod Mod;   // the GameBanana mod holding it (Held)
        }

        static Answer Say(Kind kind, string text, GameBanana.Mod mod = null) { return new Answer { Kind = kind, Text = text, Mod = mod }; }

        static string By(GameBanana.Mod mod) { return mod.Name + (string.IsNullOrEmpty(mod.Author) ? "" : " by " + mod.Author); }

        public static Answer Costume(string fighter, int slot)
        {
            string name = Fighters.Name(fighter) + " " + slot.ToString("D2");
            if (slot >= 1 && slot < Catalog.FirstCustomCostume) return Say(Kind.Game, name + " is one of the game's own costumes");
            if (slot < 1 || slot > Catalog.LastSlot) return Say(Kind.Invalid, "A costume slot is " + Catalog.FirstCustomCostume + " to " + Catalog.LastSlot);
            GameBanana.Mod mod;
            if (SharedCodes.HeldBy.TryGetValue(SharedCodes.Code(fighter, slot), out mod)) return Say(Kind.Held, name + " is taken on GameBanana: " + By(mod), mod);
            if (Catalog.NotFree(false, null, slot)) return Say(Kind.NotFree, name + " isn't free");
            return Say(Kind.Free, name + " is free");
        }

        public static Answer Stage(string code)
        {
            code = (code ?? "").Trim().ToUpperInvariant();
            if (!Regex.IsMatch(code, "^[A-Z0-9]{3}$")) return Say(Kind.Invalid, "A stage code is three letters or digits");
            if (!Stages.IsCustomCode(code)) return Say(Kind.Game, code + " is one of the game's own stages");
            GameBanana.Mod mod;
            if (SharedCodes.HeldBy.TryGetValue("stage " + code, out mod)) return Say(Kind.Held, "Stage " + code + " is taken on GameBanana: " + By(mod), mod);
            if (Catalog.NotFree(true, code, 0)) return Say(Kind.NotFree, "Stage " + code + " isn't free");
            return Say(Kind.Free, "Stage " + code + " is free");
        }

        /// <summary>The warning for putting `item` in its code (Package codes, Add Mod From File), or null: free. A code a
        /// GameBanana mod holds doesn't warn for that mod itself.</summary>
        public static string Warning(Item item)
        {
            if (item.IsColor) return null;   // new colors aren't shared: any free number will do
            Answer answer = item.IsStage ? Stage(item.Code) : Costume(item.Fighter, item.Slot);
            if (answer.Kind == Kind.Free || answer.Kind == Kind.Invalid) return null;
            if (answer.Kind == Kind.Held && (item.Id ?? "").StartsWith("gamebanana:" + answer.Mod.Id + ":")) return null;
            return answer.Text;
        }
    }
}
