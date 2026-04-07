using System.Collections.ObjectModel;
using System.Collections.Specialized;
using Microsoft.UI;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace CalendarApp.Presentation;

public sealed partial class MainPage : Page
{
    private MainViewModel? _viewModel;

    public MainPage()
    {
        this.InitializeComponent();
        this.DataContextChanged += OnDataContextChanged;

        // Set in code-behind because Uno's XAML parser rejects space-separated
        // ManipulationModes flags — must use the bitwise enum combination instead.
        RootGrid.ManipulationMode =
            Microsoft.UI.Xaml.Input.ManipulationModes.TranslateX |
            Microsoft.UI.Xaml.Input.ManipulationModes.TranslateInertia;
    }

    private void OnDataContextChanged(FrameworkElement sender, DataContextChangedEventArgs args)
    {
        if (args.NewValue is MainViewModel vm && vm != _viewModel)
        {
            // Unsubscribe from old
            if (_viewModel != null)
            {
                _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            }

            _viewModel = vm;
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;

            // Initial build if data already loaded
            if (_viewModel.MonthDays.Count > 0)
            {
                BuildMonthGrid(_viewModel.MonthDays);
            }
        }
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (_viewModel == null) return;

        void RunOnUI(Action action)
        {
            if (DispatcherQueue.HasThreadAccess) action();
            else DispatcherQueue.TryEnqueue(() => action());
        }

        if (e.PropertyName == nameof(MainViewModel.MonthDays))
        {
            RunOnUI(() =>
            {
                try
                {
                    BuildMonthGrid(_viewModel.MonthDays);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[CalendarApp] ERROR in BuildMonthGrid: {ex}");
                }
            });
        }
        else if (e.PropertyName == nameof(MainViewModel.YearMonths))
        {
            RunOnUI(() =>
            {
                try
                {
                    BuildYearGrid(_viewModel.YearMonths);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[CalendarApp] ERROR in BuildYearGrid: {ex}");
                }
            });
        }
    }

    private void BuildMonthGrid(ObservableCollection<CalendarDayViewModel> days)
    {
        MonthGrid.Children.Clear();

        for (int i = 0; i < days.Count && i < 42; i++)
        {
            var day = days[i];
            int row = i / 7;
            int col = i % 7;

            var cell = CreateDayCell(day);
            Grid.SetRow(cell, row);
            Grid.SetColumn(cell, col);
            MonthGrid.Children.Add(cell);
        }
    }

    private Border CreateDayCell(CalendarDayViewModel day)
    {
        var border = new Border
        {
            BorderBrush = new SolidColorBrush(Colors.LightGray),
            BorderThickness = new Thickness(0, 0, 1, 1),
            MinHeight = 80,
            Padding = new Thickness(4),
        };

        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        // Day number - top left
        var dayNumberContainer = new Grid
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(2, 2, 0, 0)
        };

        if (day.IsToday)
        {
            var todayCircle = new Microsoft.UI.Xaml.Shapes.Ellipse
            {
                Width = 28,
                Height = 28,
                Fill = (Brush)Application.Current.Resources["PrimaryBrush"]
            };
            dayNumberContainer.Children.Add(todayCircle);
        }

        var dayText = new TextBlock
        {
            Text = day.Day.ToString(),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = 14,
            Opacity = day.IsCurrentMonth ? 1.0 : 0.4,
            Foreground = day.IsToday
                ? new SolidColorBrush(Colors.White)
                : (Brush)Application.Current.Resources["TextFillColorPrimaryBrush"]
        };

        if (day.IsToday)
        {
            dayText.FontWeight = new Windows.UI.Text.FontWeight { Weight = 700 };
        }

        dayNumberContainer.Children.Add(dayText);
        Grid.SetRow(dayNumberContainer, 0);
        grid.Children.Add(dayNumberContainer);

        // Info area: cross reference, day start, lunar conjunction
        var infoPanel = new StackPanel { Margin = new Thickness(4, 0, 0, 0) };

        if (day.HasCrossReference)
        {
            infoPanel.Children.Add(new TextBlock
            {
                Text = day.CrossReference ?? "",
                FontSize = 9,
                Foreground = new SolidColorBrush(Colors.Gray),
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxLines = 1
            });
        }

        if (day.HasDayStartDisplay)
        {
            infoPanel.Children.Add(new TextBlock
            {
                Text = day.DayStartDisplay ?? "",
                FontSize = 9,
                Foreground = new SolidColorBrush(Colors.Gray),
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxLines = 1
            });
        }

        if (day.HasLunarConjunctionDisplay)
        {
            infoPanel.Children.Add(new TextBlock
            {
                Text = day.LunarConjunctionDisplay ?? "",
                FontSize = 9,
                Foreground = (Brush)Application.Current.Resources["PrimaryBrush"],
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxLines = 1
            });
        }

        if (day.HasCrescentIlluminationDisplay)
        {
            infoPanel.Children.Add(new TextBlock
            {
                Text = day.CrescentIlluminationDisplay ?? "",
                FontSize = 9,
                Foreground = (Brush)Application.Current.Resources["PrimaryBrush"],
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxLines = 1
            });
        }

        if (day.HasAstronomicalEventDisplay)
        {
            infoPanel.Children.Add(new TextBlock
            {
                Text = day.AstronomicalEventDisplay ?? "",
                FontSize = 9,
                Foreground = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 0, 160, 120)),
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxLines = 1
            });
        }

        foreach (var holName in day.HolidayDisplays)
        {
            infoPanel.Children.Add(new Border
            {
                Background = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(40, 200, 130, 0)),
                BorderBrush = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 200, 130, 0)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(4, 2, 4, 2),
                Margin = new Thickness(0, 2, 0, 0),
                Child = new TextBlock
                {
                    Text = holName,
                    FontSize = 9,
                    Foreground = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 200, 130, 0)),
                    FontWeight = new Windows.UI.Text.FontWeight { Weight = 600 },
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    MaxLines = 1
                }
            });
        }

        if (infoPanel.Children.Count > 0)
        {
            Grid.SetRow(infoPanel, 1);
            grid.Children.Add(infoPanel);
        }

        // Events
        if (day.Events.Count > 0)
        {
            var eventsPanel = new StackPanel { Margin = new Thickness(0, 4, 0, 0) };
            foreach (var evt in day.Events)
            {
                var capturedEvt = evt; // capture for lambda
                var eventBorder = new Border
                {
                    Background = ParseHexBrush(evt.DisplayColorHex),
                    CornerRadius = new CornerRadius(2),
                    Padding = new Thickness(4, 2, 4, 2),
                    Margin = new Thickness(0, 1, 0, 0)
                };
                eventBorder.Child = new TextBlock
                {
                    Text = evt.Title,
                    Foreground = ParseHexBrush(evt.TextColorHex),
                    FontSize = 10,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    MaxLines = 1
                };
                eventBorder.Tapped += (s, e) =>
                {
                    e.Handled = true;
                    _viewModel?.EditEventCommand.Execute(capturedEvt);
                };
                eventsPanel.Children.Add(eventBorder);
            }
            Grid.SetRow(eventsPanel, 2);
            grid.Children.Add(eventsPanel);
        }

        border.Child = grid;
        return border;
    }

    // ========== YEAR JUMP ==========

    private void OnJumpToYearClicked(object sender, RoutedEventArgs e)
    {
        if (_viewModel == null) return;
        if (NavigationTitleButton?.Flyout is Flyout flyout)
            flyout.Hide();
        _viewModel.JumpToYearCommand.Execute(null);
    }

    // ========== YEAR VIEW ==========

    private void BuildYearGrid(ObservableCollection<YearMonthViewModel> months)
    {
        YearGrid.Children.Clear();

        for (int i = 0; i < months.Count; i++)
        {
            int row = i / 4;
            int col = i % 4;

            var miniMonth = CreateMiniMonthPanel(months[i]);
            Grid.SetRow(miniMonth, row);
            Grid.SetColumn(miniMonth, col);
            YearGrid.Children.Add(miniMonth);
        }
    }

    private Border CreateMiniMonthPanel(YearMonthViewModel month)
    {
        var border = new Border
        {
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12),
            Margin = new Thickness(4),
        };

        var panel = new StackPanel();

        // Month name header
        var monthName = new TextBlock
        {
            Text = month.MonthName,
            FontSize = 14,
            FontWeight = new Windows.UI.Text.FontWeight { Weight = 600 },
            Margin = new Thickness(0, 0, 0, 8)
        };
        panel.Children.Add(monthName);

        // Day of week headers (S M T W T F S)
        var headerGrid = new Grid();
        string[] dayHeaders = { "S", "M", "T", "W", "T", "F", "S" };
        for (int c = 0; c < 7; c++)
        {
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var header = new TextBlock
            {
                Text = dayHeaders[c],
                FontSize = 10,
                HorizontalAlignment = HorizontalAlignment.Center,
                Foreground = new SolidColorBrush(Colors.Gray)
            };
            Grid.SetColumn(header, c);
            headerGrid.Children.Add(header);
        }
        panel.Children.Add(headerGrid);

        // Days grid - calculate rows needed
        int totalCells = month.Days.Count;
        int rowCount = (totalCells + 6) / 7; // ceiling division

        var daysGrid = new Grid { Margin = new Thickness(0, 4, 0, 0) };
        for (int c = 0; c < 7; c++)
            daysGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (int r = 0; r < rowCount; r++)
            daysGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        for (int i = 0; i < month.Days.Count; i++)
        {
            var day = month.Days[i];
            int row = i / 7;
            int col = i % 7;

            var cell = new Grid
            {
                Width = 24,
                Height = 24,
                HorizontalAlignment = HorizontalAlignment.Center
            };

            if (day.IsToday)
            {
                var circle = new Microsoft.UI.Xaml.Shapes.Ellipse
                {
                    Width = 20,
                    Height = 20,
                    Fill = (Brush)Application.Current.Resources["PrimaryBrush"],
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                cell.Children.Add(circle);
            }
            else if (day.IsAstronomicalEvent)
            {
                // Teal ring to mark equinox/solstice days
                var ring = new Microsoft.UI.Xaml.Shapes.Ellipse
                {
                    Width = 20,
                    Height = 20,
                    Stroke = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 0, 160, 120)),
                    StrokeThickness = 1.5,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                cell.Children.Add(ring);
            }
            else if (day.IsHoliday)
            {
                // Gold ring to mark Biblical holy days
                var ring = new Microsoft.UI.Xaml.Shapes.Ellipse
                {
                    Width = 20,
                    Height = 20,
                    Stroke = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 200, 130, 0)),
                    StrokeThickness = 1.5,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                cell.Children.Add(ring);
            }

            if (!day.IsBlank)
            {
                var dayText = new TextBlock
                {
                    Text = day.DayDisplay,
                    FontSize = 11,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = day.IsToday
                        ? new SolidColorBrush(Colors.White)
                        : day.IsAstronomicalEvent
                            ? new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 0, 160, 120))
                            : day.IsHoliday
                                ? new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 200, 130, 0))
                                : (Brush)Application.Current.Resources["TextFillColorPrimaryBrush"]
                };
                cell.Children.Add(dayText);

                if (day.HasEvents)
                {
                    var dot = new Microsoft.UI.Xaml.Shapes.Ellipse
                    {
                        Width = 4,
                        Height = 4,
                        Fill = (Brush)Application.Current.Resources["PrimaryBrush"],
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Bottom
                    };
                    cell.Children.Add(dot);
                }

                if (day.IsHoliday && !string.IsNullOrEmpty(day.HolidayName))
                    ToolTipService.SetToolTip(cell, day.HolidayName);
            }

            Grid.SetRow(cell, row);
            Grid.SetColumn(cell, col);
            daysGrid.Children.Add(cell);
        }

        panel.Children.Add(daysGrid);
        border.Child = panel;
        return border;
    }

    // ── Event click handler (shared by Week / Day / Agenda XAML DataTemplates) ──

    private void OnEventTapped(object sender, TappedRoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is FrameworkElement fe && fe.DataContext is CalendarEventViewModel evt)
            _viewModel?.EditEventCommand.Execute(evt);
    }

    // ── Swipe / manipulation gestures ─────────────────────────────────────

    /// <summary>
    /// Handles horizontal swipe gestures on the main calendar area.
    /// Left swipe → next period (next month / week / day).
    /// Right swipe → previous period.
    /// Works on Android/iOS touch and Windows touch screens.
    /// The ManipulationMode="TranslateX" on the root Grid ensures this only
    /// fires for horizontal gestures; vertical scrolling in child controls
    /// (month ScrollViewer, etc.) is unaffected.
    /// </summary>
    private void OnCalendarSwipeCompleted(object sender, ManipulationCompletedRoutedEventArgs e)
    {
        const double minSwipeDistance = 60; // dp
        var dx = e.Cumulative.Translation.X;
        if (Math.Abs(dx) < minSwipeDistance) return;

        if (dx < 0)
            _viewModel?.NextCommand.Execute(null);
        else
            _viewModel?.PreviousCommand.Execute(null);
    }

    // ── Color helpers ──────────────────────────────────────────────────────

    private static SolidColorBrush ParseHexBrush(string hex)
    {
        hex = hex.TrimStart('#');
        if (hex.Length == 6)
        {
            try
            {
                var r = Convert.ToByte(hex[0..2], 16);
                var g = Convert.ToByte(hex[2..4], 16);
                var b = Convert.ToByte(hex[4..6], 16);
                return new SolidColorBrush(Windows.UI.Color.FromArgb(255, r, g, b));
            }
            catch { }
        }
        return new SolidColorBrush(Colors.DodgerBlue);
    }
}
