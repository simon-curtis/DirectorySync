using System;
using System.Collections.Generic;
using System.Text;

namespace DirectorySync.Models
{
    public enum MatchStatus
    {
        NotProcessed,
        RightUnique,
        LeftUnique,
        LeftIsNewer,
        FilesAreDifferent,
        FilesAreTheSame,
        RightIsNewer
    }
}
