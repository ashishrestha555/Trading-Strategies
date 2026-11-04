#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Xml.Serialization;
using System.Linq;
using System.Windows.Media;
using NinjaTrader.Cbi;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Gui.SuperDom;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.Core.FloatingPoint;
using NinjaTrader.NinjaScript.DrawingTools;
#endregion

namespace NinjaTrader.NinjaScript.Indicators
{
    public class ICT2022Indicator : Indicator
    {
        public class SwingPoint
        {
            public double Price;
            public int BarIndex;
            public bool IsHigh;
            public bool Swept;
        }

        private List<SwingPoint> _swings;
        private double _priorDayHigh, _priorDayLow, _priorDayMid, _priorDayOpen, _priorDayClose;
        private double _currentDayHigh = double.MinValue;
        private double _currentDayLow = double.MaxValue;
        private double _currentDayOpen = 0;

        private double _asiaHigh, _asiaLow, _londonHigh, _londonLow;
        private bool _insideAsia, _insideLondon;

        [Range(1, 20)]
        [NinjaScriptProperty]
        [Display(Name = "SwingLookbackBars", Order = 0, GroupName = "Parameters")]
        public int SwingLookbackBars { get; set; }

        [Range(0, 100)]
        [NinjaScriptProperty]
        [Display(Name = "FVGOpacity", Order = 1, GroupName = "Parameters")]
        public int FVGOpacity { get; set; }

        [XmlIgnore]
        [Display(Name = "Bullish/PDH Color", Order = 2, GroupName = "Visuals")]
        public Brush BullishBrush { get; set; }

        [XmlIgnore]
        [Display(Name = "Bearish/PDL Color", Order = 3, GroupName = "Visuals")]
        public Brush BearishBrush { get; set; }

        [XmlIgnore]
        [Display(Name = "Session Color", Order = 4, GroupName = "Visuals")]
        public Brush SessionBrush { get; set; }

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "ICT 2022 Indicator";
                Calculate = Calculate.OnBarClose;
                IsOverlay = true;
                SwingLookbackBars = 5;
                FVGOpacity = 35;
                BullishBrush = Brushes.LimeGreen;
                BearishBrush = Brushes.Red;
                SessionBrush = Brushes.DodgerBlue;
            }
            else if (State == State.DataLoaded)
            {
                _swings = new List<SwingPoint>();
            }
        }

        protected override void OnBarUpdate()
        {
            if (CurrentBar < SwingLookbackBars * 2 + 10) return;

            TrackDailyLevels();
            TrackSessions();
            DetectSwings();
            DetectBOSandCHOCH();
            DetectFVGs();
            DetectSweep();
        }

        private void TrackDailyLevels()
        {
            if (Bars.IsFirstBarOfSession)
            {
                _priorDayHigh = _currentDayHigh;
                _priorDayLow = _currentDayLow;
                _priorDayMid = (_priorDayHigh + _priorDayLow) / 2;
                _priorDayOpen = _currentDayOpen;
                _priorDayClose = Close[1];

                _currentDayOpen = Open[0];
                _currentDayHigh = High[0];
                _currentDayLow = Low[0];

                if (_priorDayHigh != double.MinValue) DrawDailyLevels();
            }
            else
            {
                _currentDayHigh = Math.Max(_currentDayHigh, High[0]);
                _currentDayLow = Math.Min(_currentDayLow, Low[0]);
            }
        }

        private void DrawDailyLevels()
        {
            string tag = "DL_" + CurrentBar;

            DrawLevel(tag + "_PDH", "PDH", _priorDayHigh, BullishBrush);
            DrawLevel(tag + "_PDL", "PDL", _priorDayLow, BearishBrush);
            DrawLevel(tag + "_PDM", "PDM", _priorDayMid, Brushes.Gray);
            DrawLevel(tag + "_PDO", "PDO", _priorDayOpen, Brushes.Black);
            DrawLevel(tag + "_PDC", "PDC", _priorDayClose, Brushes.Red);
        }

        private void DrawLevel(string tag, string label, double price, Brush brush)
        {
            if (price <= 0 || price == double.MinValue) return;
            
            // Draw lines using barsAgo (0 to -10)
            Draw.Line(this, tag, false, 0, price, -10, price, brush, DashStyleHelper.Dash, 1);
            // Using the simplest Draw.Text overload to avoid UI namespace errors
            Draw.Text(this, tag + "_txt", label, -10, price, brush);
        }

        private void TrackSessions()
        {
            int hour = Time[0].Hour;
            bool asiaNow = (hour >= 20 || hour < 1);
            if (asiaNow)
            {
                if (!_insideAsia) { _insideAsia = true; _asiaHigh = High[0]; _asiaLow = Low[0]; }
                _asiaHigh = Math.Max(_asiaHigh, High[0]);
                _asiaLow = Math.Min(_asiaLow, Low[0]);
            }
            else if (_insideAsia) { DrawSession("Asia", _asiaHigh, _asiaLow); _insideAsia = false; }

            bool londonNow = (hour >= 2 && hour < 5);
            if (londonNow)
            {
                if (!_insideLondon) { _insideLondon = true; _londonHigh = High[0]; _londonLow = Low[0]; }
                _londonHigh = Math.Max(_londonHigh, High[0]);
                _londonLow = Math.Min(_londonLow, Low[0]);
            }
            else if (_insideLondon) { DrawSession("London", _londonHigh, _londonLow); _insideLondon = false; }
        }

        private void DrawSession(string prefix, double high, double low)
        {
            string tag = prefix + "_" + CurrentBar;
            Draw.Line(this, tag + "_H", false, 20, high, 0, high, SessionBrush, DashStyleHelper.Solid, 1);
            Draw.Line(this, tag + "_L", false, 20, low, 0, low, SessionBrush, DashStyleHelper.Solid, 1);
        }

        private void DetectSwings()
        {
            bool isHigh = High[SwingLookbackBars] >= MAX(High, SwingLookbackBars * 2 + 1)[0];
            bool isLow = Low[SwingLookbackBars] <= MIN(Low, SwingLookbackBars * 2 + 1)[0];

            if (isHigh)
                _swings.Add(new SwingPoint() { Price = High[SwingLookbackBars], BarIndex = CurrentBar - SwingLookbackBars, IsHigh = true });
            if (isLow)
                _swings.Add(new SwingPoint() { Price = Low[SwingLookbackBars], BarIndex = CurrentBar - SwingLookbackBars, IsHigh = false });
        }

        private void DetectBOSandCHOCH()
        {
            if (_swings.Count < 2) return;
            SwingPoint last = _swings[_swings.Count - 1];
            SwingPoint prev = _swings[_swings.Count - 2];

            if (last.BarIndex != CurrentBar - SwingLookbackBars) return;

            if (last.IsHigh && last.Price > prev.Price)
                Draw.Text(this, "BOS_" + last.BarIndex, "BOS↑", SwingLookbackBars, last.Price + TickSize * 5, BullishBrush);
            
            if (!last.IsHigh && last.Price < prev.Price)
                Draw.Text(this, "BOS_" + last.BarIndex, "BOS↓", SwingLookbackBars, last.Price - TickSize * 5, BearishBrush);
        }

        private void DetectFVGs()
        {
            if (Low[0] > High[2])
                Draw.Rectangle(this, "FVG_U" + CurrentBar, false, 2, Low[0], 0, High[2], 
                    Brushes.Transparent, new SolidColorBrush(Color.FromArgb((byte)(FVGOpacity * 2.55), 0, 255, 0)), 1);
            else if (High[0] < Low[2])
                Draw.Rectangle(this, "FVG_D" + CurrentBar, false, 2, Low[2], 0, High[0], 
                    Brushes.Transparent, new SolidColorBrush(Color.FromArgb((byte)(FVGOpacity * 2.55), 255, 0, 0)), 1);
        }

        private void DetectSweep()
        {
            if (_swings.Count == 0) return;
            foreach (var sw in _swings)
            {
                if (sw.Swept) continue;
                if (sw.IsHigh && High[0] > sw.Price)
                {
                    sw.Swept = true;
                    Draw.Dot(this, "SwpH" + sw.BarIndex, false, 0, High[0], Brushes.Gold);
                }
                else if (!sw.IsHigh && Low[0] < sw.Price)
                {
                    sw.Swept = true;
                    Draw.Dot(this, "SwpL" + sw.BarIndex, false, 0, Low[0], Brushes.Gold);
                }
            }
        }
    }
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private ICT2022Indicator[] cacheICT2022Indicator;
		public ICT2022Indicator ICT2022Indicator(int swingLookbackBars, int fVGOpacity)
		{
			return ICT2022Indicator(Input, swingLookbackBars, fVGOpacity);
		}

		public ICT2022Indicator ICT2022Indicator(ISeries<double> input, int swingLookbackBars, int fVGOpacity)
		{
			if (cacheICT2022Indicator != null)
				for (int idx = 0; idx < cacheICT2022Indicator.Length; idx++)
					if (cacheICT2022Indicator[idx] != null && cacheICT2022Indicator[idx].SwingLookbackBars == swingLookbackBars && cacheICT2022Indicator[idx].FVGOpacity == fVGOpacity && cacheICT2022Indicator[idx].EqualsInput(input))
						return cacheICT2022Indicator[idx];
			return CacheIndicator<ICT2022Indicator>(new ICT2022Indicator(){ SwingLookbackBars = swingLookbackBars, FVGOpacity = fVGOpacity }, input, ref cacheICT2022Indicator);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.ICT2022Indicator ICT2022Indicator(int swingLookbackBars, int fVGOpacity)
		{
			return indicator.ICT2022Indicator(Input, swingLookbackBars, fVGOpacity);
		}

		public Indicators.ICT2022Indicator ICT2022Indicator(ISeries<double> input , int swingLookbackBars, int fVGOpacity)
		{
			return indicator.ICT2022Indicator(input, swingLookbackBars, fVGOpacity);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.ICT2022Indicator ICT2022Indicator(int swingLookbackBars, int fVGOpacity)
		{
			return indicator.ICT2022Indicator(Input, swingLookbackBars, fVGOpacity);
		}

		public Indicators.ICT2022Indicator ICT2022Indicator(ISeries<double> input , int swingLookbackBars, int fVGOpacity)
		{
			return indicator.ICT2022Indicator(input, swingLookbackBars, fVGOpacity);
		}
	}
}

#endregion
