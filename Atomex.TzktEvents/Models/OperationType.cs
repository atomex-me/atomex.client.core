using System.ComponentModel;

namespace Atomex.TzktEvents.Models
{
    /***
     * Describes types of for operations (https://api.tzkt.io/#section/SubscribeToOperations)
     * and stores their api values for each type in <see cref="Description"/> attribute.
     */
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
