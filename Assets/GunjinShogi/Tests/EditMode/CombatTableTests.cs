using NUnit.Framework;
using GunjinShogi.Core;
using static GunjinShogi.Core.StandardPieces;

namespace GunjinShogi.Core.Tests
{
    public class CombatTableTests
    {
        // 参照用の勝敗表（行の駒から見た結果）。o=勝ち x=負け ==相打ち -=対戦なし
        // 順序：大将 中将 少将 大佐 中佐 少佐 大尉 中尉 少尉 飛行機 タンク 騎兵 工兵 スパイ 地雷
        // ※ 工兵がタンクに勝つ版（TankBeatsEngineer = false）
        static readonly string[] Reference =
        {
            "=oooooooooooox=", // 大将
            "x=ooooooooooooo".Substring(0, 14) + "=", // 中将
            "xx=oooooooooooo".Substring(0, 14) + "=", // 少将
            "xxx=oooooxxooo=", // 大佐
            "xxxx=ooooxxooo=", // 中佐
            "xxxxx=oooxxooo=", // 少佐
            "xxxxxx=ooxxooo=", // 大尉
            "xxxxxxx=oxxooo=", // 中尉
            "xxxxxxxx=xxooo=", // 少尉
            "xxxoooooo=ooooo", // 飛行機
            "xxxoooooox=oxo=", // タンク
            "xxxxxxxxxxx=oo=", // 騎兵
            "xxxxxxxxxxox=oo", // 工兵
            "oxxxxxxxxxxxx==", // スパイ
            "=========x==x=-", // 地雷
        };

        static Outcome Parse(char c) =>
            c == 'o' ? Outcome.Win : c == 'x' ? Outcome.Lose : Outcome.Both;

        [Test]
        public void ReferenceTableMatches_WhenEngineerBeatsTank()
        {
            var rules = StandardRules.Create23(new StandardRuleOptions { TankBeatsEngineer = false });
            for (int a = 0; a < Reference.Length; a++)
            {
                Assert.AreEqual(15, Reference[a].Length, $"参照表の行 {a} の長さ");
                for (int b = 0; b < Reference.Length; b++)
                {
                    char c = Reference[a][b];
                    if (c == '-') continue;
                    Assert.AreEqual(Parse(c), rules.Combat.Get(a, b),
                        $"{rules.Piece(a).Name} 対 {rules.Piece(b).Name}");
                }
            }
        }

        [Test]
        public void TankBeatsEngineer_IsDefaultOn()
        {
            var o = new StandardRuleOptions();
            Assert.IsTrue(o.TankBeatsEngineer);
            var rules = StandardRules.Create23();
            Assert.AreEqual(Outcome.Win, rules.Combat.Get(Tank, Engineer));
            Assert.AreEqual(Outcome.Lose, rules.Combat.Get(Engineer, Tank));
        }

        [Test]
        public void Table_IsAntisymmetric()
        {
            var t = StandardRules.Create23().Combat;
            for (int a = 0; a < t.Size; a++)
            for (int b = 0; b < t.Size; b++)
                Assert.AreEqual(CombatTable.Inverse(t.Get(a, b)), t.Get(b, a), $"{a} vs {b}");
        }

        [Test]
        public void MineSurvivesWin_TurnsTradesIntoWins()
        {
            var rules = StandardRules.Create23(new StandardRuleOptions { MineSurvivesWin = true });
            Assert.AreEqual(Outcome.Win, rules.Combat.Get(Mine, General));
            Assert.AreEqual(Outcome.Win, rules.Combat.Get(Mine, Tank));
            Assert.AreEqual(Outcome.Lose, rules.Combat.Get(Mine, Plane));
            Assert.AreEqual(Outcome.Lose, rules.Combat.Get(Mine, Engineer));
        }

        [Test]
        public void StandardRules_AreValid_And23PiecesFillCamp()
        {
            var rules = StandardRules.Create23();
            CollectionAssert.IsEmpty(rules.Validate());
            Assert.AreEqual(23, rules.PiecesPerPlayer);
            var topo = new BoardTopology(rules.Board);
            int n = 0;
            foreach (var _ in topo.CampNodes(0)) n++;
            Assert.AreEqual(23, n);
        }
    }
}
