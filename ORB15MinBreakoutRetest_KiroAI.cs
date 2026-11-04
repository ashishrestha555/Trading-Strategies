#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Xml.Serialization;
using NinjaTrader.Cbi;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Gui.SuperDom;
using NinjaTrader.Gui.Tools;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.Core.FloatingPoint;
using NinjaTrader.NinjaScript.Indicators;
using NinjaTrader.NinjaScript.DrawingTools;
#endregion

namespace NinjaTrader.NinjaScript.Strategies
{
    public class ORB15MinBreakoutRetest_KiroAI : Strategy
    {
        #region Variables
        // Session times (EST)
        private TimeSpan sessionStart = new TimeSpan(9, 30, 0);
        private TimeSpan sessionEnd = new TimeSpan(9, 45, 0);
        private TimeSpan tradeEnd = new TimeSpan(16, 0, 0);
        
        // Initial range variables
        private double initHigh = double.MinValue;
        private double initLow = double.MaxValue;
        private bool rangeEstablished = false;
        private int rangeStartBar = -1;
        private int sessionEndBar = -1;
        
        // Breakout state
        private bool breakoutUp = false;
        private bool breakoutDn = false;
        private int bullBreakoutBar = -1;
        private int bearBreakoutBar = -1;
        private double breakoutBuyLow = 0;
        private double breakoutSellHigh = 0;
        
        // Trade state
        private bool waitingForNewBreakoutUp = true;
        private bool waitingForNewBreakoutDn = true;
        private bool inLongTrade = false;
        private bool inShortTrade = false;
        
        // Day tracking
        private DateTime currentTradeDate = DateTime.MinValue;
        
        // Pivot points storage
        private double pivotPoint = 0;
        private double r1 = 0, r2 = 0, r3 = 0;
        private double s1 = 0, s2 = 0, s3 = 0;
        private double previousHigh = 0;
        private double previousLow = 0;
        private double previousClose = 0;
        private bool pivotCalculated = false;
        private int pivotStartBar = -1;
        
        // EMA
        private EMA ema21;
        #endregion

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = @"15 Minute Opening Range Breakout with Retest Entry";
                Name = "ORB15MinBreakoutRetest_KiroAI";
                Calculate = Calculate.OnBarClose;
                EntriesPerDirection = 1;
                EntryHandling = EntryHandling.AllEntries;
                IsExitOnSessionCloseStrategy = true;
                ExitOnSessionCloseSeconds = 30;
                IsFillLimitOnTouch = false;
                MaximumBarsLookBack = MaximumBarsLookBack.TwoHundredFiftySix;
                OrderFillResolution = OrderFillResolution.Standard;
                Slippage = 0;
                StartBehavior = StartBehavior.WaitUntilFlat;
                TimeInForce = TimeInForce.Gtc;
                TraceOrders = false;
                RealtimeErrorHandling = RealtimeErrorHandling.StopCancelClose;
                StopTargetHandling = StopTargetHandling.PerEntryExecution;
                BarsRequiredToTrade = 20;
                IsInstantiatedOnEachOptimizationIteration = true;
                
                // User parameters
                BreakoutClosePercent = 30.0;
                RiskRewardRatio = 2.0;
                ShowPivotPoints = true;
                ShowEMA = true;
                EMALength = 21;
            }
            else if (State == State.Configure)
            {
            }
            else if (State == State.DataLoaded)
            {
                try
                {
                    ema21 = EMA(EMALength);
                    if (ShowEMA && ema21 != null)
                    {
                        ema21.Plots[0].Brush = Brushes.Orange;
                        AddChartIndicator(ema21);
                    }
                }
                catch (Exception ex)
                {
                    Print("Error loading EMA: " + ex.Message);
                }
            }
        }

        protected override void OnBarUpdate()
        {
            try
            {
                if (CurrentBar < BarsRequiredToTrade)
                    return;
                
                DateTime currentTime = Time[0];
                TimeSpan currentTimeOfDay = currentTime.TimeOfDay;
                
                // Reset on new day
                if (currentTradeDate.Date != currentTime.Date)
                {
                    ResetDailyVariables();
                    currentTradeDate = currentTime.Date;
                    
                    // Calculate pivot points from previous day
                    if (CurrentBar > 1)
                    {
                        CalculatePivotPoints();
                        if (pivotCalculated)
                        {
                            pivotStartBar = CurrentBar;
                        }
                    }
                }
                
                // Mark session end bar for line drawing
                if (currentTimeOfDay >= tradeEnd && sessionEndBar == -1)
                {
                    sessionEndBar = CurrentBar;
                }
                
                // Establish initial range (9:30 - 9:45 AM)
                if (currentTimeOfDay >= sessionStart && currentTimeOfDay < sessionEnd)
                {
                    if (rangeStartBar == -1)
                        rangeStartBar = CurrentBar;
                    
                    if (initHigh == double.MinValue)
                        initHigh = High[0];
                    else
                        initHigh = Math.Max(initHigh, High[0]);
                    
                    if (initLow == double.MaxValue)
                        initLow = Low[0];
                    else
                        initLow = Math.Min(initLow, Low[0]);
                    
                    rangeEstablished = true;
                }
                
                // Draw initial range lines (only during trading hours of current day)
                if (rangeEstablished && rangeStartBar != -1 && 
                    currentTimeOfDay >= sessionStart && currentTimeOfDay < tradeEnd &&
                    initHigh != double.MinValue && initLow != double.MaxValue)
                {
                    string tagHigh = "InitHigh" + currentTradeDate.ToString("yyyyMMdd");
                    string tagLow = "InitLow" + currentTradeDate.ToString("yyyyMMdd");
                    
                    int barsAgo = CurrentBar - rangeStartBar;
                    
                    Draw.Line(this, tagHigh, false, barsAgo, initHigh, 0, initHigh, 
                        Brushes.Blue, DashStyleHelper.Solid, 2);
                    
                    Draw.Line(this, tagLow, false, barsAgo, initLow, 0, initLow, 
                        Brushes.Red, DashStyleHelper.Solid, 2);
                }
                
                // Draw pivot points (only during trading hours of current day)
                if (ShowPivotPoints && pivotCalculated && pivotStartBar != -1 &&
                    currentTimeOfDay >= sessionStart && currentTimeOfDay < tradeEnd)
                {
                    DrawPivotLines();
                }
                
                // Only trade during trading hours
                if (currentTimeOfDay < sessionEnd || currentTimeOfDay >= tradeEnd)
                    return;
                
                if (!rangeEstablished || initHigh == double.MinValue || initLow == double.MaxValue)
                    return;
                
                // Calculate breakout candle strength
                double breakoutCandleRange = High[0] - Low[0];
                bool validBreakoutRange = breakoutCandleRange > 0;
                
                bool bullishCloseLocation = validBreakoutRange && 
                    ((High[0] - Close[0]) / breakoutCandleRange) <= (BreakoutClosePercent / 100.0);
                
                bool bearishCloseLocation = validBreakoutRange && 
                    ((Close[0] - Low[0]) / breakoutCandleRange) <= (BreakoutClosePercent / 100.0);
                
                // Detect breakouts
                bool bullBreakout = Close[0] > initHigh && Close[1] <= initHigh && 
                    waitingForNewBreakoutUp && !breakoutUp && bullishCloseLocation;
                
                bool bearBreakout = Close[0] < initLow && Close[1] >= initLow && 
                    waitingForNewBreakoutDn && !breakoutDn && bearishCloseLocation;
                
                if (bullBreakout)
                {
                    breakoutUp = true;
                    bullBreakoutBar = CurrentBar;
                    breakoutBuyLow = Low[0];
                    waitingForNewBreakoutUp = false;
                    Draw.Text(this, "BreakoutUp" + CurrentBar, "Breakout Up", 0, High[0] + TickSize * 2, Brushes.Yellow);
                }
                
                if (bearBreakout)
                {
                    breakoutDn = true;
                    bearBreakoutBar = CurrentBar;
                    breakoutSellHigh = High[0];
                    waitingForNewBreakoutDn = false;
                    Draw.Text(this, "BreakoutDn" + CurrentBar, "Breakout Down", 0, Low[0] - TickSize * 2, Brushes.Yellow);
                }
                
                // Retest entry logic
                bool bullRetestCondition = breakoutUp && CurrentBar > bullBreakoutBar && 
                    Low[0] <= initHigh && Close[0] > initHigh && 
                    Close[0] > Open[0] && Position.MarketPosition == MarketPosition.Flat;
                
                bool bearRetestCondition = breakoutDn && CurrentBar > bearBreakoutBar && 
                    High[0] >= initLow && Close[0] < initLow && 
                    Close[0] < Open[0] && Position.MarketPosition == MarketPosition.Flat;
                
                // Enter long on bullish retest
                if (bullRetestCondition)
                {
                    double entryPrice = Close[0];
                    double stopLoss = Math.Min(breakoutBuyLow, Low[0]);
                    double risk = entryPrice - stopLoss;
                    
                    if (risk > 0)
                    {
                        double target = entryPrice + RiskRewardRatio * risk;
                        
                        EnterLong("RetestLong");
                        SetStopLoss("RetestLong", CalculationMode.Price, stopLoss, false);
                        SetProfitTarget("RetestLong", CalculationMode.Price, target);
                        
                        Draw.Text(this, "BuySignal" + CurrentBar, "B", 0, High[0] + TickSize * 2, Brushes.Green);
                        
                        breakoutUp = false;
                        inLongTrade = true;
                    }
                }
                
                // Enter short on bearish retest
                if (bearRetestCondition)
                {
                    double entryPrice = Close[0];
                    double stopLoss = Math.Max(breakoutSellHigh, High[0]);
                    double risk = stopLoss - entryPrice;
                    
                    if (risk > 0)
                    {
                        double target = entryPrice - RiskRewardRatio * risk;
                        
                        EnterShort("RetestShort");
                        SetStopLoss("RetestShort", CalculationMode.Price, stopLoss, false);
                        SetProfitTarget("RetestShort", CalculationMode.Price, target);
                        
                        Draw.Text(this, "SellSignal" + CurrentBar, "S", 0, Low[0] - TickSize * 2, Brushes.Red);
                        
                        breakoutDn = false;
                        inShortTrade = true;
                    }
                }
            }
            catch (Exception ex)
            {
                Print("OnBarUpdate Error: " + ex.Message);
            }
        }
        
        protected override void OnPositionUpdate(Position position, double averagePrice, 
            int quantity, MarketPosition marketPosition)
        {
            if (marketPosition == MarketPosition.Flat)
            {
                waitingForNewBreakoutUp = true;
                waitingForNewBreakoutDn = true;
                bullBreakoutBar = -1;
                bearBreakoutBar = -1;
                breakoutUp = false;
                breakoutDn = false;
                inLongTrade = false;
                inShortTrade = false;
            }
        }
        
        private void ResetDailyVariables()
        {
            initHigh = double.MinValue;
            initLow = double.MaxValue;
            rangeEstablished = false;
            rangeStartBar = -1;
            sessionEndBar = -1;
            breakoutUp = false;
            breakoutDn = false;
            bullBreakoutBar = -1;
            bearBreakoutBar = -1;
            breakoutBuyLow = 0;
            breakoutSellHigh = 0;
            waitingForNewBreakoutUp = true;
            waitingForNewBreakoutDn = true;
            inLongTrade = false;
            inShortTrade = false;
            pivotStartBar = -1;
        }
        
        private void CalculatePivotPoints()
        {
            try
            {
                DateTime targetDate = Time[0].Date.AddDays(-1);
                
                double high = double.MinValue;
                double low = double.MaxValue;
                double close = 0;
                
                for (int i = 1; i < Math.Min(500, CurrentBar); i++)
                {
                    if (Time[i].Date == targetDate)
                    {
                        high = Math.Max(high, High[i]);
                        low = Math.Min(low, Low[i]);
                        close = Close[i];
                    }
                    else if (Time[i].Date < targetDate)
                    {
                        break;
                    }
                }
                
                if (high == double.MinValue || low == double.MaxValue)
                    return;
                
                previousHigh = high;
                previousLow = low;
                previousClose = close;
                
                pivotPoint = (previousHigh + previousLow + previousClose) / 3.0;
                r1 = 2 * pivotPoint - previousLow;
                s1 = 2 * pivotPoint - previousHigh;
                r2 = pivotPoint + (previousHigh - previousLow);
                s2 = pivotPoint - (previousHigh - previousLow);
                r3 = previousHigh + 2 * (pivotPoint - previousLow);
                s3 = previousLow - 2 * (previousHigh - pivotPoint);
                
                pivotCalculated = true;
            }
            catch (Exception ex)
            {
                Print("CalculatePivotPoints Error: " + ex.Message);
            }
        }
        
        private void DrawPivotLines()
        {
            try
            {
                string dateTag = currentTradeDate.ToString("yyyyMMdd");
                int barsAgo = CurrentBar - pivotStartBar;
                
                Draw.Line(this, "Pivot" + dateTag, false, barsAgo, pivotPoint, 0, pivotPoint, 
                    Brushes.Orange, DashStyleHelper.Solid, 1);
                Draw.Text(this, "PivotText" + dateTag, "P (" + pivotPoint.ToString("F2") + ")", 
                    0, pivotPoint, Brushes.Orange);
                
                Draw.Line(this, "R1" + dateTag, false, barsAgo, r1, 0, r1, 
                    Brushes.Magenta, DashStyleHelper.Solid, 1);
                Draw.Text(this, "R1Text" + dateTag, "R1 (" + r1.ToString("F2") + ")", 
                    0, r1, Brushes.Magenta);
                
                Draw.Line(this, "R2" + dateTag, false, barsAgo, r2, 0, r2, 
                    Brushes.Magenta, DashStyleHelper.Solid, 1);
                Draw.Text(this, "R2Text" + dateTag, "R2 (" + r2.ToString("F2") + ")", 
                    0, r2, Brushes.Magenta);
                
                Draw.Line(this, "R3" + dateTag, false, barsAgo, r3, 0, r3, 
                    Brushes.Magenta, DashStyleHelper.Solid, 1);
                Draw.Text(this, "R3Text" + dateTag, "R3 (" + r3.ToString("F2") + ")", 
                    0, r3, Brushes.Magenta);
                
                Draw.Line(this, "S1" + dateTag, false, barsAgo, s1, 0, s1, 
                    Brushes.Green, DashStyleHelper.Solid, 1);
                Draw.Text(this, "S1Text" + dateTag, "S1 (" + s1.ToString("F2") + ")", 
                    0, s1, Brushes.Green);
                
                Draw.Line(this, "S2" + dateTag, false, barsAgo, s2, 0, s2, 
                    Brushes.Green, DashStyleHelper.Solid, 1);
                Draw.Text(this, "S2Text" + dateTag, "S2 (" + s2.ToString("F2") + ")", 
                    0, s2, Brushes.Green);
                
                Draw.Line(this, "S3" + dateTag, false, barsAgo, s3, 0, s3, 
                    Brushes.Green, DashStyleHelper.Solid, 1);
                Draw.Text(this, "S3Text" + dateTag, "S3 (" + s3.ToString("F2") + ")", 
                    0, s3, Brushes.Green);
            }
            catch (Exception ex)
            {
                Print("DrawPivotLines Error: " + ex.Message);
            }
        }

        #region Properties
        [NinjaScriptProperty]
        [Range(1, 50)]
        [Display(Name="Breakout Candle Close Strength %", Description="Percentage of candle range for valid breakout", Order=1, GroupName="Parameters")]
        public double BreakoutClosePercent
        { get; set; }

        [NinjaScriptProperty]
        [Range(1, 10)]
        [Display(Name="Risk/Reward Ratio", Description="Target profit as multiple of risk", Order=2, GroupName="Parameters")]
        public double RiskRewardRatio
        { get; set; }
        
        [NinjaScriptProperty]
        [Display(Name="Show Pivot Points", Description="Display pivot point levels", Order=3, GroupName="Display")]
        public bool ShowPivotPoints
        { get; set; }
        
        [NinjaScriptProperty]
        [Display(Name="Show EMA", Description="Display 21 EMA", Order=4, GroupName="Display")]
        public bool ShowEMA
        { get; set; }
        
        [NinjaScriptProperty]
        [Range(1, 200)]
        [Display(Name="EMA Length", Description="Period for EMA calculation", Order=5, GroupName="Parameters")]
        public int EMALength
        { get; set; }
        #endregion
    }
}