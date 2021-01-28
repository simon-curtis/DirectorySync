using MahApps.Metro.IconPacks;
using System.ComponentModel;
using System.Windows.Media;
using System.Runtime.CompilerServices;

namespace DirectorySync.Models
{
    public class ComparisonResult : INotifyPropertyChanged
    {
        public string Name { get; set; } = "";
        public long LeftSize { get; set; }
        public string? LeftDate { get; set; }
        public long RightSize { get; set; }
        public string? RightDate { get; set; }
        public PackIconIoniconsKind ActionIcon { get; private set; } = PackIconIoniconsKind.None;
        public Brush ActionIconColour { get; private set; } = Brushes.Gray;

        private ResolutionAction resolution = ResolutionAction.Nothing;

        public ResolutionAction Resolution
        {
            get => resolution;
            set
            {
                resolution = value;
                var style = resolution switch
                {
                    ResolutionAction.Nothing => (PackIconIoniconsKind.None, Brushes.Gray),
                    ResolutionAction.CopyLeft => (PackIconIoniconsKind.ArrowRoundBackiOS, Brushes.Blue),
                    ResolutionAction.CopyRight => (PackIconIoniconsKind.ArrowRoundForwardiOS, Brushes.Green),
                    ResolutionAction.Delete => (PackIconIoniconsKind.CloseCircleiOS, Brushes.Red),
                    _ => (PackIconIoniconsKind.None, Brushes.Gray)
                };
                ActionIcon = style.Item1;
                ActionIconColour = style.Item2;
                NotifyManualPropertyChanged(nameof(ActionIcon));
                NotifyManualPropertyChanged(nameof(ActionIconColour));
            }
        }

        private MatchStatus status = MatchStatus.NotProcessed;
        public MatchStatus Status
        {
            get => status; set
            {
                status = value;
                NotifyPropertyChanged();
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void NotifyManualPropertyChanged(string propertyName = "") =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

        private void NotifyPropertyChanged([CallerMemberName] string propertyName = "") =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}