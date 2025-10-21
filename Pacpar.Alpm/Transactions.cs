namespace Pacpar.Alpm.Native
{
    public class Transactions
    {
        public enum alpm_transflag_t
        {
            ALPM_TRANS_FLAG_NODEPS = 1,
            ALPM_TRANS_FLAG_NOSAVE = 1 << 2,
            ALPM_TRANS_FLAG_NODEPVERSION = 1 << 3,
            ALPM_TRANS_FLAG_CASCADE = 1 << 4,
            ALPM_TRANS_FLAG_RECURSE = 1 << 5,
            ALPM_TRANS_FLAG_DBONLY = 1 << 6,
            ALPM_TRANS_FLAG_NOHOOKS = 1 << 7,
            ALPM_TRANS_FLAG_ALLDEPS = 1 << 8,
            ALPM_TRANS_FLAG_DOWNLOADONLY = 1 << 9,
            ALPM_TRANS_FLAG_NOSCRIPTLET = 1 << 10,
            ALPM_TRANS_FLAG_NOCONFLICTS = 1 << 11,
            ALPM_TRANS_FLAG_NEEDED = 1 << 13,
            ALPM_TRANS_FLAG_ALLEXPLICIT = 1 << 14,
            ALPM_TRANS_FLAG_UNNEEDED = 1 << 15,
            ALPM_TRANS_FLAG_RECURSEALL = 1 << 16,
            ALPM_TRANS_FLAG_NOLOCK = 1 << 17
        }
    }
}
