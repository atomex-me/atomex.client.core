using System.ComponentModel;

namespace Atomex.TzktEvents.Models
{
    public enum OperationType
    {
        [Description("transaction")]
        Transaction,

        [Description("origination")]
        Origination,

        [Description("delegation")]
        Delegation,

        [Description("reveal")]
        Reveal,

        [Description("register_constant")]
        RegisterConstant,

        [Description("set_deposits_limit")]
        SetDepositsLimit,

        [Description("double_baking")]
        DoubleBaking,

        [Description("double_endorsing")]
        DoubleEndorsing,

        [Description("double_preendorsing")]
        DoublePreendorsing,

        [Description("nonce_revelation")]
        NonceRevelation,

        [Description("activation")]
        Activation,

        [Description("proposal")]
        Proposal,

        [Description("ballot")]
        Ballot,

        [Description("endorsement")]
        Endorsement,

        [Description("preendorsement")]
        Preendorsement
    }
}
