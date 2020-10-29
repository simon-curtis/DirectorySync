using System;
using System.Collections.Generic;
using System.Text;

namespace FileCompare
{
    public enum MatchStatus
    {
        NotProcessed,
        TargetMissing,
        OriginalIsNewer,
        FilesAreDifferent,
        FilesAreTheSame,
        TargetIsNewer
    }
}
