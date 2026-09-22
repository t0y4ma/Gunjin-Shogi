using System;
using NUnit.Framework;
using GunjinShogi.Core;

namespace GunjinShogi.Core.Tests
{
    public class CodecTests
    {
[Test]
        public void RuleCode_RoundTrips_RandomSettings()
        {
            var rng = new Random(1);
            for (int i = 0; i < 500; i++)
            {
                var o = new StandardRuleOptions();
                foreach (var f in RuleFields.All)
                    if (rng.Next(3) == 0) f.Set(o, rng.Next(f.ChoiceCount));
                var code = RuleCodec.Encode(o);
                Assert.IsTrue(RuleCodec.TryDecode(code.ToLowerInvariant(), out var d), code);
                Assert.AreEqual(code, RuleCodec.Encode(d));
                foreach (var f in RuleFields.All) Assert.AreEqual(f.Get(o), f.Get(d), code + " / " + f.Key);
            }
        }

[Test]
        public void RuleCode_Format()
        {
            // すべて既定なら "0"、選択肢の index 0 は必ず既定
            Assert.AreEqual("0", RuleCodec.Encode(new StandardRuleOptions()));
            foreach (var f in RuleFields.All) Assert.AreEqual(0, f.Get(new StandardRuleOptions()), f.Key.ToString());

            // 変えた項目だけを「英字＋番号」で表の順に連ねる
            var o = new StandardRuleOptions { TankBeatsEngineer = false, SecondLtFlagWin = true, PlaneSideways = PlaneSideways.Unlimited };
            Assert.AreEqual("A1E2K1", RuleCodec.Encode(o));
            Assert.AreEqual(3, RuleCodec.DiffCount(o));

            Assert.IsTrue(RuleCodec.TryDecode("k1a1", out var d));
            Assert.IsFalse(d.TankBeatsEngineer);
            Assert.IsTrue(d.SecondLtFlagWin);

            Assert.IsFalse(RuleCodec.TryDecode("", out _));
            Assert.IsFalse(RuleCodec.TryDecode("A", out _));
            Assert.IsFalse(RuleCodec.TryDecode("A0", out _), "既定値は書かない");
            Assert.IsFalse(RuleCodec.TryDecode("A2", out _), "選択肢の範囲外");
            Assert.IsFalse(RuleCodec.TryDecode("A1A1", out _), "同じ項目が2回");
            Assert.IsFalse(RuleCodec.TryDecode("Z1", out _), "存在しない項目");
        }

        [Test]
        public void SetupCode_RoundTrips_AndMirrorsForSecondPlayer()
        {
            var rules = StandardRules.Create23();
            var topo = new BoardTopology(rules.Board);
            var rng = new Random(2);
            for (int i = 0; i < 50; i++)
            {
                var s0 = RandomSetup.Generate(rules, topo, 0, rng);
                var code = SetupCodec.Encode(topo, 0, s0);
                Assert.AreEqual(23, code.Length);

                // 同じコードを後手で読むと、盤を180度回した同じ形になる
                var s1 = SetupCodec.Decode(rules, topo, 1, code);
                CollectionAssert.IsEmpty(SetupValidator.Validate(rules, topo, 1, s1));
                Assert.AreEqual(code, SetupCodec.Encode(topo, 1, s1));

                var back = SetupCodec.Decode(rules, topo, 0, code);
                CollectionAssert.AreEquivalent(s0, back);
            }
            Assert.IsNull(SetupCodec.Decode(rules, topo, 0, "ABC"));
        }

        [Test]
        public void CompositionKey_IgnoresMovementOptions()
        {
            var a = StandardRules.Create23();
            var b = StandardRules.Create23(new StandardRuleOptions { FlagCanMove = true, PlaneSideways = PlaneSideways.None });
            Assert.AreEqual(SetupCodec.CompositionKey(a), SetupCodec.CompositionKey(b));
        }
    }
}
