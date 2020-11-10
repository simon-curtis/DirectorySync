using System;
using System.Collections.Generic;
using System.Text;

namespace FileCompare
{
    public enum MatchStatus
    {
        NotProcessed,
        MissingAndCreatedBeforeFolder,
        MissingAndCreatedAfterFolder,
        OriginalIsNewer,
        FilesAreDifferent,
        FilesAreTheSame,
        TargetIsNewer
    }
}
