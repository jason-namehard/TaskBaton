namespace TaskBaton.Protocol;

/// <summary>Stable diagnostic codes for handoff/1. Additive codes may be introduced; existing meanings do not change.</summary>
public static class ProtocolDiagnosticCodes
{
    // Argument / parse failures (exit 2).
    public const string ArgumentInvalid = "protocol.argument_invalid";
    public const string FileNotFound = "protocol.file_not_found";
    public const string InvalidFrontmatter = "protocol.invalid_frontmatter";
    public const string InvalidJson = "protocol.invalid_json";
    public const string SchemaUnavailable = "protocol.schema_unavailable";

    // Handoff document.
    public const string HandoffVersionUnsupported = "handoff.version_unsupported";
    public const string HandoffFieldRequired = "handoff.field_required";
    public const string HandoffFieldInvalid = "handoff.field_invalid";
    public const string HandoffFromEqualsTo = "handoff.from_equals_to";
    public const string HandoffTimestampNotUtc = "handoff.timestamp_not_utc";
    public const string HandoffPathUnsafe = "handoff.path_unsafe";
    public const string HandoffSectionRequired = "handoff.section_required";
    public const string HandoffSessionExactRequired = "handoff.session_exact_required";
    public const string HandoffSessionNotFound = "handoff.session_not_found";
    public const string HandoffSessionMismatch = "handoff.session_mismatch";
    public const string HandoffSchemaInvalid = "handoff.schema_invalid";

    // Receipt document.
    public const string ReceiptVersionUnsupported = "receipt.version_unsupported";
    public const string ReceiptFieldRequired = "receipt.field_required";
    public const string ReceiptFieldInvalid = "receipt.field_invalid";
    public const string ReceiptIdMismatch = "receipt.id_mismatch";
    public const string ReceiptNeedsReplyRequired = "receipt.needs_reply_required";
    public const string ReceiptNextActionRequired = "receipt.next_action_required";
    public const string ReceiptNextActionNotAllowed = "receipt.next_action_not_allowed";
    public const string ReceiptResultRequired = "receipt.result_required";
    public const string ReceiptOpenItemsRequired = "receipt.open_items_required";
    public const string ReceiptOpenItemsNotAllowed = "receipt.open_items_not_allowed";
    public const string ReceiptDeliverableRequired = "receipt.deliverable_required";
    public const string ReceiptErrorRequired = "receipt.error_required";
    public const string ReceiptOutcomeRequiresDone = "receipt.outcome_requires_done";
    public const string ReceiptTimestampNotUtc = "receipt.timestamp_not_utc";
    public const string ReceiptPathUnsafe = "receipt.path_unsafe";
    public const string ReceiptSchemaInvalid = "receipt.schema_invalid";
    public const string ReceiptReviewFinalInvalid = "receipt.review_final_invalid";

    // L3 state machine.
    public const string ReceiptDuplicateIdempotent = "receipt.duplicate_idempotent";
    public const string ReceiptIdempotencyConflict = "receipt.idempotency_conflict";
    public const string ReceiptLateAccepted = "receipt.late_accepted";
    public const string ReceiptOldAttempt = "receipt.old_attempt";
    public const string ReceiptConflictingFinal = "receipt.conflicting_final";
    public const string ReceiptCancelled = "receipt.cancelled";

    // L5 cleanup.
    public const string CleanupOutsideRoot = "cleanup.outside_root";
    public const string CleanupNotOwned = "cleanup.not_owned";
    public const string CleanupActiveReference = "cleanup.active_reference";
    public const string CleanupProtected = "cleanup.protected";
    public const string CleanupNotExpired = "cleanup.not_expired";
}
