using MediatR;
using Microsoft.AspNetCore.Mvc;
using PayFlow.API.Filters;
using PayFlow.API.Requests;
using PayFlow.Application.Features.Payments.Commands.CancelPayment;
using PayFlow.Application.Features.Payments.Commands.CreatePayment;
using PayFlow.Application.Features.Payments.Commands.Webhook;
using PayFlow.Application.Features.Payments.Queries.GetPayment;
using PayFlow.Application.Features.Payments.Queries.ListPayments;

namespace PayFlow.API.Controllers;

[ApiController]
[Route("payments")]
[Produces("application/json")]
public class PaymentsController : ControllerBase
{
    private readonly IMediator _mediator;

    public PaymentsController(IMediator mediator) => _mediator = mediator;

    [HttpPost]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create([FromBody] CreatePaymentRequest request, CancellationToken ct)
    {
        var command = new CreatePaymentCommand(request.CustomerId, request.MerchantId, request.Amount, request.Currency);
        var paymentId = await _mediator.Send(command, ct);
        return CreatedAtAction(nameof(GetById), new { id = paymentId }, new { paymentId });
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
        => Ok(await _mediator.Send(new GetPaymentQuery(id), ct));

    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] ListPaymentsQuery query, CancellationToken ct)
        => Ok(await _mediator.Send(query, ct));

    [HttpPost("{id:guid}/cancel")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken ct)
    {
        await _mediator.Send(new CancelPaymentCommand(id), ct);
        return NoContent();
    }

    [HttpPost("webhook")]
    [ServiceFilter(typeof(WebhookSignatureFilter))]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Webhook([FromBody] WebhookPayload payload, CancellationToken ct)
    {
        await _mediator.Send(new WebhookCommand(payload.PaymentId, payload.Success, payload.ErrorMessage), ct);
        return Ok();
    }
}
