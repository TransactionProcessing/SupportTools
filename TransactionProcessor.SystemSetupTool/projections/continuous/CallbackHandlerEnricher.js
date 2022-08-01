//starttestsetup
var fromStreams = fromStreams || require('../../node_modules/@transactionprocessing/esprojection-testing-framework').scope.fromStreams;
var emit = emit || require('../../node_modules/@transactionprocessing/esprojection-testing-framework').scope.emit;
//endtestsetup

fromStreams("$ce-EstateAggregate", "$et-CallbackReceivedEvent")
    .when({
        $init: function (s, e)
        {
            return {
                estates: [],
                debug: []
            }
        },
        "EstateCreatedEvent": function (s, e) {
            s.estates.push({
                estateId: e.data.estateId,
                estateName: e.data.estateName
            });
        },
        "EstateReferenceAllocatedEvent": function (s, e) {
            var estateIndex = s.estates.findIndex(element => element.estateId === e.data.estateId);
            s.estates[estateIndex].reference = e.data.estateReference;
        },
        "CallbackReceivedEvent": function (s, e) {
            // find the estate from the reference
            if (s.debug === undefined) {
                s.debug = [];
            }
            var ref = e.data.reference.split("-"); // Element 0 is estate reference, Element 1 is merchant reference
            var estate = s.estates.find(element => element.reference === ref[0]);
            if (estate !== undefined && estate !== null) {
                var enrichedEvent = createEnrichedEvent(e, estate);

                // Emit the enriched event
                emit(getStreamName(estate, e), "CallbackReceivedEnrichedEvent", enrichedEvent);
            }
            else {
                var enrichedEvent = createEnrichedEvent(e);
                // Emit the enriched event
                emit(getStreamName(estate, e), "CallbackReceivedEnrichedWithNoEstateEvent", enrichedEvent);
            }
        }
    });

function createEnrichedEvent(originalEvent, estate) {
    var enrichedEvent = {};
    if (estate !== undefined && estate !== null) {
        enrichedEvent = {
            typeString: originalEvent.data.typeString,
            messageFormat: originalEvent.data.messageFormat,
            callbackMessage: originalEvent.data.callbackMessage,
            estateId: estate.estateId,
            reference: originalEvent.data.reference
        };
    }
    else {
        enrichedEvent = {
            typeString: originalEvent.data.typeString,
            messageFormat: originalEvent.data.messageFormat,
            callbackMessage: originalEvent.data.callbackMessage,
            reference: originalEvent.data.reference
        };
    }

    return enrichedEvent;
}

function getStreamName(estate, e) {
    var streamName = "";
    if (e.data.destination === "EstateManagement") {
        streamName += "EstateManagementSubscriptionStream_";
    }

    // Add the estate name
    if (estate !== undefined && estate !== null) {
        streamName += estate.estateName.replace(/ /g, "");
    }
    else {
        streamName += "UnknownEstate";
    }

    return streamName;

}