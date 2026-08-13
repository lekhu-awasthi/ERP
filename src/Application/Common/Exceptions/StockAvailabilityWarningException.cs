namespace ErpApp.Application.Common.Exceptions;

/// <summary>
/// Distinct from ConflictException on purpose -- this signals a confirmable warning (architecture-
/// spec.md §3.5's "Warn-and-allow" behavior), not a hard block. Maps to HTTP 422 so the Angular
/// client can tell "resubmit with OverrideWarning=true to proceed anyway" apart from a genuine 409
/// conflict it can't route around. See ApproveInvoiceCommandHandler and
/// Application.Sales.Stock.FifoStockAvailabilityPolicy.
/// </summary>
public sealed class StockAvailabilityWarningException(string message) : Exception(message);
