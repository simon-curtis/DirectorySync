namespace FileCompare
{
    public class ComparisonResult
    {
        public string LeftName { get; set; }
        public long LeftSize { get; set; }
        public string LeftDate { get; set; }
        public string RightDate { get; set; }
        public long RightSize { get; set; }
        public string RightName { get; set; }
        public MatchStatus Status { get; set; } = MatchStatus.NotProcessed;
    }
}
