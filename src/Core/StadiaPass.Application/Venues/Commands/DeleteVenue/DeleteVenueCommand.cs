using MediatR;

namespace StadiaPass.Application.Venues.Commands.DeleteVenue;

public sealed record DeleteVenueCommand(Guid Id) : IRequest;
