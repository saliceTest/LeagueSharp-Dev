﻿#region License

/*
 Copyright 2014 - 2015 Nikita Bernthaler
 Smite.cs is part of SFXUtility.

 SFXUtility is free software: you can redistribute it and/or modify
 it under the terms of the GNU General Public License as published by
 the Free Software Foundation, either version 3 of the License, or
 (at your option) any later version.

 SFXUtility is distributed in the hope that it will be useful,
 but WITHOUT ANY WARRANTY; without even the implied warranty of
 MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
 GNU General Public License for more details.

 You should have received a copy of the GNU General Public License
 along with SFXUtility. If not, see <http://www.gnu.org/licenses/>.
*/

#endregion License

namespace SFXUtility.Features.Activators
{
    #region

    using System;
    using System.Collections.Generic;
    using System.Drawing;
    using System.Linq;
    using Classes;
    using Data;
    using LeagueSharp;
    using LeagueSharp.Common;
    using SFXLibrary.Extensions.NET;
    using SFXLibrary.Extensions.SharpDX;
    using SFXLibrary.Logger;
    using Utils = SFXLibrary.Utils;

    #endregion

    internal class Smite : Base
    {
        public const float SmiteRange = 570f;
        private readonly List<Jungle.Camp> _camps = new List<Jungle.Camp>();
        private readonly List<HeroSpell> _heroSpells = new List<HeroSpell>();
        private Obj_AI_Minion _currentMinion;
        private bool _delayActive;
        private string[] _mobNames = new string[0];
        private Activators _parent;
        private Spell _smiteSpell;

        public override bool Enabled
        {
            get { return !Unloaded && _parent != null && _parent.Enabled && Menu != null && Menu.Item(Name + "Enabled").GetValue<bool>(); }
        }

        public override string Name
        {
            get { return Global.Lang.Get("F_Smite"); }
        }

        protected override void OnEnable()
        {
            Game.OnUpdate += OnGameUpdate;
            Drawing.OnDraw += OnDrawingDraw;
            base.OnEnable();
        }

        protected override void OnDisable()
        {
            Game.OnUpdate -= OnGameUpdate;
            Drawing.OnDraw -= OnDrawingDraw;
            base.OnDisable();
        }

        private void OnParentInitialized(object sender, EventArgs eventArgs)
        {
            try
            {
                if (_parent.Menu == null)
                    return;

                Menu = new Menu(Name, Name);

                var drawingMenu = Menu.AddSubMenu(new Menu(Global.Lang.Get("G_Drawing"), Name + "Drawing"));
                drawingMenu.AddItem(
                    new MenuItem(drawingMenu.Name + "DamageColor",
                        Global.Lang.Get("G_Damage") + " " + Global.Lang.Get("G_Indicator") + " " + Global.Lang.Get("G_Color")).SetValue(Color.SkyBlue));
                drawingMenu.AddItem(
                    new MenuItem(drawingMenu.Name + "DamageIndicator", Global.Lang.Get("G_Damage") + " " + Global.Lang.Get("G_Indicator")).SetValue(
                        false));

                var spellMenu = Menu.AddSubMenu(new Menu(Global.Lang.Get("G_Spell"), Name + "Spell"));

                var championMenu =
                    spellMenu.AddSubMenu(new Menu(ObjectManager.Player.ChampionName, spellMenu.Name + ObjectManager.Player.ChampionName));
                var championPriorityMenu = championMenu.AddSubMenu(new Menu(Global.Lang.Get("G_Priority"), championMenu.Name + "Priority"));
                var spells =
                    ObjectManager.Player.Spellbook.Spells.Where(s => new[] {SpellSlot.Q, SpellSlot.W, SpellSlot.E, SpellSlot.R}.Contains(s.Slot))
                        .ToList();
                var index = spells.Count;

                foreach (var spell in spells.Where(s => s.SData.TargettingType != SpellDataTargetType.Self))
                {
                    var spellName = Utils.GetEnumName(spell.Slot);
                    championPriorityMenu.AddItem(
                        new MenuItem(championPriorityMenu.Name + spellName, spellName).SetValue(new Slider(index--, 1, spells.Count)));

                    var championSpellMenu = championMenu.AddSubMenu(new Menu(spellName, championMenu.Name + spellName));

                    var championDrawingMenu = championSpellMenu.AddSubMenu(new Menu(Global.Lang.Get("G_Drawing"), championSpellMenu.Name + "Drawing"));
                    championDrawingMenu.AddItem(
                        new MenuItem(championDrawingMenu.Name + "UseableColor", Global.Lang.Get("G_Useable") + " " + Global.Lang.Get("G_Color"))
                            .SetValue(Color.Blue));
                    championDrawingMenu.AddItem(
                        new MenuItem(championDrawingMenu.Name + "UnusableColor", Global.Lang.Get("G_Unusable") + " " + Global.Lang.Get("G_Color"))
                            .SetValue(Color.Gray));
                    championDrawingMenu.AddItem(
                        new MenuItem(championDrawingMenu.Name + "Thickness", Global.Lang.Get("G_Thickness")).SetValue(new Slider(2, 1, 10)));
                    championDrawingMenu.AddItem(new MenuItem(championDrawingMenu.Name + "Range", Global.Lang.Get("G_Range")).SetValue(false));
                    championSpellMenu.AddItem(
                        new MenuItem(championSpellMenu.Name + "IsSkillshot", Global.Lang.Get("G_Is") + " " + Global.Lang.Get("G_Skillshot")).SetValue(
                            false));
                    championSpellMenu.AddItem(new MenuItem(championSpellMenu.Name + "Collision", Global.Lang.Get("G_Collision")).SetValue(false));
                    championSpellMenu.AddItem(
                        new MenuItem(championSpellMenu.Name + "MinHitChance", Global.Lang.Get("G_Minimum") + " " + Global.Lang.Get("G_HitChance"))
                            .SetValue(new StringList(Global.Lang.GetList("Smite_MinHitChanceList"), 1)));
                    championSpellMenu.AddItem(
                        new MenuItem(championSpellMenu.Name + "DamageType", Global.Lang.Get("G_Damage") + " " + Global.Lang.Get("G_Type")).SetValue(
                            new StringList(Global.Lang.GetList("Smite_DamageTypeList"))));
                    championSpellMenu.AddItem(
                        new MenuItem(championSpellMenu.Name + "SkillshotType", Global.Lang.Get("G_Skillshot") + " " + Global.Lang.Get("G_Type"))
                            .SetValue(new StringList(Global.Lang.GetList("Smite_SkillshotTypeList"))));
                    championSpellMenu.AddItem(new MenuItem(championSpellMenu.Name + "Enabled", Global.Lang.Get("G_Enabled")).SetValue(false));

                    var heroSpell = new HeroSpell(spell.Slot, Menu.Item(championSpellMenu.Name + "IsSkillshot").GetValue<bool>(),
                        Menu.Item(championSpellMenu.Name + "Collision").GetValue<bool>(),
                        (HitChance) (Menu.Item(championSpellMenu.Name + "MinHitChance").GetValue<StringList>().SelectedIndex + 3),
                        (TargetSelector.DamageType) (Menu.Item(championSpellMenu.Name + "DamageType").GetValue<StringList>().SelectedIndex),
                        (SkillshotType) (Menu.Item(championSpellMenu.Name + "SkillshotType").GetValue<StringList>().SelectedIndex),
                        Menu.Item(championSpellMenu.Name + "Enabled").GetValue<bool>())
                    {
                        Priority = Menu.Item(championPriorityMenu.Name + spellName).GetValue<Slider>().Value,
                        UseableColor = Menu.Item(championDrawingMenu.Name + "UseableColor").GetValue<Color>(),
                        UnusableColor = Menu.Item(championDrawingMenu.Name + "UnusableColor").GetValue<Color>(),
                        Thickness = Menu.Item(championDrawingMenu.Name + "Thickness").GetValue<Slider>().Value,
                        Drawing = Menu.Item(championDrawingMenu.Name + "Range").GetValue<bool>()
                    };

                    Menu.Item(championPriorityMenu.Name + spellName).ValueChanged +=
                        (o, args) => heroSpell.Priority = args.GetNewValue<Slider>().Value;
                    Menu.Item(championSpellMenu.Name + "IsSkillshot").ValueChanged += (o, args) => heroSpell.IsSkillshot = args.GetNewValue<bool>();
                    Menu.Item(championSpellMenu.Name + "Collision").ValueChanged += (o, args) => heroSpell.Collision = args.GetNewValue<bool>();
                    Menu.Item(championSpellMenu.Name + "MinHitChance").ValueChanged +=
                        (o, args) => heroSpell.MinHitChance = (HitChance) (args.GetNewValue<StringList>().SelectedIndex + 3);
                    Menu.Item(championSpellMenu.Name + "DamageType").ValueChanged +=
                        (o, args) => heroSpell.DamageType = (TargetSelector.DamageType) (args.GetNewValue<StringList>().SelectedIndex);
                    Menu.Item(championSpellMenu.Name + "SkillshotType").ValueChanged +=
                        (o, args) => heroSpell.SkillshotType = (SkillshotType) (args.GetNewValue<StringList>().SelectedIndex);
                    Menu.Item(championSpellMenu.Name + "Enabled").ValueChanged += (o, args) => heroSpell.Enabled = args.GetNewValue<bool>();

                    Menu.Item(championDrawingMenu.Name + "UseableColor").ValueChanged +=
                        (o, args) => heroSpell.UseableColor = args.GetNewValue<Color>();
                    Menu.Item(championDrawingMenu.Name + "UnusableColor").ValueChanged +=
                        (o, args) => heroSpell.UnusableColor = args.GetNewValue<Color>();
                    Menu.Item(championDrawingMenu.Name + "Thickness").ValueChanged +=
                        (o, args) => heroSpell.Thickness = args.GetNewValue<Slider>().Value;
                    Menu.Item(championDrawingMenu.Name + "Range").ValueChanged += (o, args) => heroSpell.Drawing = args.GetNewValue<bool>();

                    _heroSpells.Add(heroSpell);
                }
                championMenu.AddItem(new MenuItem(championMenu.Name + "Enabled", Global.Lang.Get("G_Enabled")).SetValue(false));

                var smiteMenu = spellMenu.AddSubMenu(new Menu(Global.Lang.Get("Smite_Smite"), spellMenu.Name + "Smite"));

                var smiteDrawingMenu = smiteMenu.AddSubMenu(new Menu(Global.Lang.Get("G_Drawing"), smiteMenu.Name + "Drawing"));
                smiteDrawingMenu.AddItem(
                    new MenuItem(smiteDrawingMenu.Name + "UseableColor", Global.Lang.Get("G_Useable") + " " + Global.Lang.Get("G_Color")).SetValue(
                        Color.Blue));
                smiteDrawingMenu.AddItem(
                    new MenuItem(smiteDrawingMenu.Name + "UnusableColor", Global.Lang.Get("G_Unusable") + " " + Global.Lang.Get("G_Color")).SetValue(
                        Color.Gray));
                smiteDrawingMenu.AddItem(
                    new MenuItem(smiteDrawingMenu.Name + "Thickness", Global.Lang.Get("G_Thickness")).SetValue(new Slider(2, 1, 10)));
                smiteDrawingMenu.AddItem(new MenuItem(smiteDrawingMenu.Name + "Range", Global.Lang.Get("G_Range")).SetValue(false));

                smiteMenu.AddItem(new MenuItem(smiteMenu.Name + "Use", Global.Lang.Get("G_Use")).SetValue(false));

                Menu.AddItem(new MenuItem(Name + "SmallCamps", Global.Lang.Get("Smite_SmallCamps")).SetValue(false));

                Menu.AddItem(new MenuItem(Name + "Hotkey", Global.Lang.Get("G_Hotkey")).SetValue(new KeyBind('N', KeyBindType.Toggle)));

                Menu.AddItem(new MenuItem(Name + "Enabled", Global.Lang.Get("G_Enabled")).SetValue(false));

                Menu.Item(Name + "SmallCamps").ValueChanged +=
                    delegate(object o, OnValueChangeEventArgs args)
                    {
                        _mobNames = args.GetNewValue<bool>()
                            ? (from c in _camps from m in c.Mobs.Where(m => m.IsBig) select m.Name).ToArray()
                            : (from c in _camps.Where(c => c.IsBig) from m in c.Mobs.Where(m => m.IsBig) select m.Name).ToArray();
                    };

                _parent.Menu.AddSubMenu(Menu);

                var smiteSpell =
                    ObjectManager.Player.Spellbook.Spells.FirstOrDefault(s => s.Name.Contains("Smite", StringComparison.OrdinalIgnoreCase));
                if (smiteSpell != null)
                    _smiteSpell = new Spell(smiteSpell.Slot, SmiteRange, TargetSelector.DamageType.True);

                _camps.AddRange(Jungle.Camps.Where(c => c.MapType == Utility.Map.GetMap().Type));

                _mobNames = Menu.Item(Name + "SmallCamps").GetValue<bool>()
                    ? (from c in _camps from m in c.Mobs.Where(m => m.IsBig) select m.Name).ToArray()
                    : (from c in _camps.Where(c => c.IsBig) from m in c.Mobs.Where(m => m.IsBig) select m.Name).ToArray();

                if (!_camps.Any() || _smiteSpell == null && !_heroSpells.Any())
                    return;

                HandleEvents(_parent);
                RaiseOnInitialized();
            }
            catch (Exception ex)
            {
                Global.Logger.AddItem(new LogItem(ex));
            }
        }

        protected override void OnGameLoad(EventArgs args)
        {
            try
            {
                if (Global.IoC.IsRegistered<Activators>())
                {
                    _parent = Global.IoC.Resolve<Activators>();
                    if (_parent.Initialized)
                        OnParentInitialized(null, null);
                    else
                        _parent.OnInitialized += OnParentInitialized;
                }
            }
            catch (Exception ex)
            {
                Global.Logger.AddItem(new LogItem(ex));
            }
        }

        private void OnDrawingDraw(EventArgs args)
        {
            if (!Menu.Item(Name + "Hotkey").GetValue<KeyBind>().Active || ObjectManager.Player.IsDead)
                return;

            var minion = _currentMinion != null && _currentMinion.IsValidTarget();

            if (_smiteSpell != null && Menu.Item(Name + "SpellSmiteDrawingRange").GetValue<bool>())
            {
                Render.Circle.DrawCircle(ObjectManager.Player.Position, SmiteRange,
                    minion && _smiteSpell.IsReady() && _smiteSpell.CanCast(_currentMinion)
                        ? Menu.Item(Name + "SpellSmiteDrawingUseableColor").GetValue<Color>()
                        : Menu.Item(Name + "SpellSmiteDrawingUnusableColor").GetValue<Color>(),
                    Menu.Item(Name + "SpellSmiteDrawingThickness").GetValue<Slider>().Value);
            }
            if (Menu.Item(Name + "Spell" + ObjectManager.Player.ChampionName + "Enabled").GetValue<bool>())
            {
                foreach (var spell in _heroSpells.Where(s => s.Enabled && s.Drawing))
                {
                    Render.Circle.DrawCircle(ObjectManager.Player.Position, spell.Range,
                        spell.CanCast(_currentMinion) ? spell.UseableColor : spell.UnusableColor, spell.Thickness);
                }
            }
            if (minion && _currentMinion.IsVisible && Menu.Item(Name + "DrawingDamageIndicator").GetValue<bool>())
            {
                var pos = Drawing.WorldToScreen(_currentMinion.Position);
                Drawing.DrawText(pos.X, pos.Y + _currentMinion.BoundingRadius/2f, Menu.Item(Name + "DrawingDamageColor").GetValue<Color>(),
                    ((int) (_currentMinion.Health - ObjectManager.Player.GetSummonerSpellDamage(_currentMinion, Damage.SummonerSpell.Smite))).ToString
                        ());
            }
        }

        private void OnGameUpdate(EventArgs args)
        {
            try
            {
                if (!Menu.Item(Name + "Hotkey").GetValue<KeyBind>().Active)
                    return;

                var smiteSpellEnabled = _smiteSpell != null && Menu.Item(Name + "SpellSmiteUse").GetValue<bool>();
                var heroSpellEnabled = Menu.Item(Name + "Spell" + ObjectManager.Player.ChampionName + "Enabled").GetValue<bool>();

                if (_smiteSpell != null && smiteSpellEnabled && !heroSpellEnabled)
                {
                    if (_currentMinion == null)
                    {
                        _currentMinion = ObjectManager.Player.ServerPosition.GetMinionFastByNames(SmiteRange, _mobNames);
                    }
                    if (_smiteSpell.IsReady())
                    {
                        if (_currentMinion.IsValidTarget(SmiteRange))
                        {
                            if (ObjectManager.Player.GetSummonerSpellDamage(_currentMinion, Damage.SummonerSpell.Smite) > _currentMinion.Health)
                            {
                                _smiteSpell.Cast(_currentMinion, true);
                            }
                        }
                        else
                        {
                            _currentMinion = null;
                        }
                    }
                    return;
                }

                if (_delayActive && _currentMinion != null && _smiteSpell != null && _currentMinion.IsValid && !_currentMinion.IsDead)
                {
                    if (ObjectManager.Player.GetSummonerSpellDamage(_currentMinion, Damage.SummonerSpell.Smite) >= _currentMinion.Health &&
                        _smiteSpell.CanCast(_currentMinion))
                        _smiteSpell.Cast(_currentMinion);
                }

                HeroSpell heroSpell = null;

                _currentMinion = ObjectManager.Player.ServerPosition.GetNearestMinionByNames(_mobNames);
                if (_currentMinion != null && _smiteSpell != null)
                {
                    if (heroSpellEnabled)
                    {
                        heroSpell = _heroSpells.OrderByDescending(s => s.Priority).FirstOrDefault(s => s.Enabled && s.CanCast(_currentMinion));
                        heroSpellEnabled = heroSpell != null;
                    }

                    double totalDamage = 0;
                    if (smiteSpellEnabled && _smiteSpell.CanCast(_currentMinion))
                        totalDamage += ObjectManager.Player.GetSummonerSpellDamage(_currentMinion, Damage.SummonerSpell.Smite);
                    if (heroSpellEnabled)
                        totalDamage += heroSpell.CalculateDamage(_currentMinion, false);

                    if (totalDamage >= _currentMinion.Health)
                    {
                        if (heroSpellEnabled)
                        {
                            heroSpell.Cast(_currentMinion);
                            if (smiteSpellEnabled && _smiteSpell.CanCast(_currentMinion))
                            {
                                _delayActive = true;
                                Utility.DelayAction.Add((int) heroSpell.CalculateHitDelay(_currentMinion), delegate
                                {
                                    if (_smiteSpell.CanCast(_currentMinion))
                                        _smiteSpell.Cast(_currentMinion);
                                    _delayActive = false;
                                });
                            }
                        }
                        else if (smiteSpellEnabled && _smiteSpell.CanCast(_currentMinion))
                            _smiteSpell.Cast(_currentMinion);
                    }
                }
            }
            catch (Exception ex)
            {
                Global.Logger.AddItem(new LogItem(ex));
            }
        }
    }

    internal class HeroSpell
    {
        private Color _unusableColor = Color.Gray;
        private Color _useableColor = Color.Teal;

        public HeroSpell(SpellSlot slot, bool isSkillshot, bool collision, HitChance minHitChance, TargetSelector.DamageType damageType,
            SkillshotType skillshotType, bool enabled)
        {
            Enabled = enabled;

            Spell = new Spell(slot, float.MaxValue, damageType);
            Collision = collision;
            MinHitChance = minHitChance;
            DamageType = damageType;

            var spellData = ObjectManager.Player.Spellbook.GetSpell(slot);
            Delay = spellData.SData.CastFrame/30;
            Speed = spellData.SData.MissileSpeed;
            Range = (spellData.SData.CastRange > spellData.SData.CastRangeDisplayOverride + 1000
                ? spellData.SData.CastRangeDisplayOverride
                : spellData.SData.CastRange) + ObjectManager.Player.BoundingRadius;
            Width = spellData.SData.LineWidth;

            SkillshotType = skillshotType;
            IsSkillshot = isSkillshot;
        }

        public Color UseableColor
        {
            get { return _useableColor; }
            set { _useableColor = value; }
        }

        public Color UnusableColor
        {
            get { return _unusableColor; }
            set { _unusableColor = value; }
        }

        public int Thickness { get; set; }
        public bool Drawing { get; set; }
        public int Priority { get; set; }
        public bool Enabled { get; set; }

        public float Delay
        {
            get { return Spell.Delay; }
            set { Spell.Delay = value; }
        }

        public float Width
        {
            get { return Spell.Width; }
            set { Spell.Width = value; }
        }

        public float Speed
        {
            get { return Spell.Speed; }
            set { Spell.Speed = value; }
        }

        public float Range
        {
            get { return Spell.Range; }
            set { Spell.Range = value; }
        }

        public bool IsSkillshot
        {
            get { return Spell.IsSkillshot; }
            set
            {
                Spell.IsSkillshot = value;
                if (IsSkillshot)
                    Spell.SetSkillshot(Delay, Width, Speed, Collision, SkillshotType);
                else
                    Spell.SetTargetted(Delay, Speed);
            }
        }

        public bool Collision
        {
            get { return Spell.Collision; }
            set { Spell.Collision = value; }
        }

        public HitChance MinHitChance
        {
            get { return Spell.MinHitChance; }
            set { Spell.MinHitChance = value; }
        }

        public TargetSelector.DamageType DamageType
        {
            get { return Spell.DamageType; }
            set { Spell.DamageType = value; }
        }

        public SkillshotType SkillshotType
        {
            get { return Spell.Type; }
            set
            {
                Spell.Type = value;
                if (IsSkillshot)
                    Spell.SetSkillshot(Delay, Width, Speed, Collision, value);
                else
                    Spell.SetTargetted(Delay, Speed);
            }
        }

        public SpellSlot Slot
        {
            get { return Spell.Slot; }
            set { Spell.Slot = value; }
        }

        public Spell Spell { get; private set; }

        public double CalculateDamage(Obj_AI_Minion minion, bool check = true)
        {
            if (check && !CanCast(minion))
                return 0;
            return Spell.GetDamage(minion);
        }

        public bool CanCast(Obj_AI_Minion minion)
        {
            return minion != null && minion.IsValid && Spell.IsReady() &&
                   Spell.GetPrediction(minion, false, Range + minion.BoundingRadius).Hitchance >= MinHitChance;
        }

        public void Cast(Obj_AI_Minion minion)
        {
            if (minion == null || Spell == null)
                return;
            Spell.Range += minion.BoundingRadius;
            Spell.Cast(minion);
            Spell.Range -= minion.BoundingRadius;
        }

        public float CalculateHitDelay(Obj_AI_Base target)
        {
            return Delay + (Speed > 0 ? ((ObjectManager.Player.ServerPosition.Distance(target.ServerPosition)/(Speed/1000))) : 0) + Game.Ping/2f;
        }
    }
}