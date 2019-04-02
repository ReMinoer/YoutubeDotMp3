using System;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Media.Animation;

namespace YoutubeDotMp3.Behaviors
{
    public class ProgressBarSmoother
    {
        static public readonly DependencyProperty EnabledProperty =
            DependencyProperty.RegisterAttached("Enabled", typeof(bool), typeof(ProgressBarSmoother), new PropertyMetadata(true, OnEnabledChanged));

        static public readonly DependencyProperty SmoothValueProperty =
            DependencyProperty.RegisterAttached("SmoothValue", typeof(double), typeof(ProgressBarSmoother), new PropertyMetadata(0.0, OnSmoothValueChanged));

        static public readonly DependencyProperty LastUpdateTimeProperty =
            DependencyProperty.RegisterAttached("LastUpdateTime", typeof(DateTime?), typeof(ProgressBarSmoother), new PropertyMetadata(null));

        static public bool GetEnabled(DependencyObject dependencyObject) => (bool)dependencyObject.GetValue(EnabledProperty);
        static public void SetEnabled(DependencyObject dependencyObject, bool value) => dependencyObject.SetValue(EnabledProperty, value);

        static public double GetSmoothValue(DependencyObject dependencyObject) => (double)dependencyObject.GetValue(SmoothValueProperty);
        static public void SetSmoothValue(DependencyObject dependencyObject, double value) => dependencyObject.SetValue(SmoothValueProperty, value);

        static private DateTime? GetLastUpdateTime(DependencyObject dependencyObject) => (DateTime?)dependencyObject.GetValue(LastUpdateTimeProperty);
        static private void SetLastUpdateTime(DependencyObject dependencyObject, DateTime? value) => dependencyObject.SetValue(LastUpdateTimeProperty, value);

        static private void OnSmoothValueChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
        {
            if (!(dependencyObject is RangeBase rangeBase))
                return;

            double newValue = (double)e.NewValue;

            // If disabled, set new value directly
            if (!GetEnabled(rangeBase))
            {
                rangeBase.Value = newValue;
                return;
            }

            // If completed, stop animation and set new value
            if (newValue >= rangeBase.Maximum)
            {
                EndAnimation(rangeBase);
                rangeBase.Value = newValue;
                return;
            }
            
            // Compute time elapsed since last refresh
            DateTime currentTime = DateTime.UtcNow;
            DateTime? lastUpdateTime = GetLastUpdateTime(rangeBase);
            
            TimeSpan refreshTime;
            if (lastUpdateTime.HasValue)
                refreshTime = currentTime - lastUpdateTime.Value;
            else
                refreshTime = TimeSpan.FromSeconds(1);

            // Keep current time as last update time
            SetLastUpdateTime(rangeBase, currentTime);
            
            // Compute projected value
            double oldValue = (double)e.OldValue;
            double progress = newValue - oldValue;
            double projectedValue = newValue + progress;

            // Animate value from current value to projected value for a duration similar to actual refresh time.
            var anim = new DoubleAnimation(rangeBase.Value, projectedValue, refreshTime);
            rangeBase.BeginAnimation(RangeBase.ValueProperty, anim, HandoffBehavior.SnapshotAndReplace);
        }

        static private void OnEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (!(d is RangeBase rangeBase))
                return;
            
            // If disabled, end animation
            bool enabled = (bool)e.NewValue;
            if (!enabled)
                EndAnimation(rangeBase);
        }

        static private void EndAnimation(RangeBase rangeBase)
        {
            // Reset last update time
            SetLastUpdateTime(rangeBase, null);

            // Stop animation (set to null)
            rangeBase.BeginAnimation(RangeBase.ValueProperty, null, HandoffBehavior.SnapshotAndReplace);
        }
    }
}