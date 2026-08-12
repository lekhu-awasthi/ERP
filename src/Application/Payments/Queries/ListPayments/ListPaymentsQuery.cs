using ErpApp.Domain.Payments;
using MediatR;

namespace ErpApp.Application.Payments.Queries.ListPayments;

public sealed record ListPaymentsQuery(Guid OrganizationId, PaymentStatus? Status) : IRequest<IReadOnlyList<Payment>>;
