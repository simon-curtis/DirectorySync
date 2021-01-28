using System;
using System.Collections.Generic;
using System.Text;

namespace DirectorySync.Models
{
    public enum MatchStatus
    {
        NotProcessed,
        MissingFromLeft,
        MissingFromRight,
        OriginalIsNewer,
        FilesAreDifferent,
        FilesAreTheSame,
        TargetIsNewer
    }
}
