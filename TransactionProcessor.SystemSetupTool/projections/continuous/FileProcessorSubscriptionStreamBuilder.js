//starttestsetup
var fromAll = fromAll || require("../../node_modules/@transactionprocessing/esprojection-testing-framework").scope.fromAll;
var linkTo = linkTo || require("../../node_modules/@transactionprocessing/esprojection-testing-framework").scope.linkTo;
//endtestsetup

isEstateEvent = (e) => { return (e.data && e.data.estateId); }
isAnEstateCreatedEvent = (e) => { return compareEventTypeSafely(e.eventType, 'EstateCreatedEvent') };
compareEventTypeSafely = (sourceEventType, targetEventType) => { return (sourceEventType.toUpperCase() === targetEventType.toUpperCase()); }
isInvalidEvent = (e) => (e === null || e === undefined || e.data === undefined);

getSupportedEventTypes = function () {
    var eventTypes = [];

    eventTypes.push('ImportLogCreatedEvent');
    eventTypes.push('FileAddedToImportLogEvent');
    eventTypes.push('FileCreatedEvent');
    eventTypes.push('FileLineAddedEvent');
    eventTypes.push('FileLineProcessingSuccessfulEvent');
    eventTypes.push('FileLineProcessingIgnoredEvent');
    eventTypes.push('FileLineProcessingFailedEvent');
    eventTypes.push('FileProcessingCompletedEvent');

    return eventTypes;
}

isARequiredEvent = (e) => {
    var supportedEvents = getSupportedEventTypes();

    var index = supportedEvents.indexOf(e.eventType);

    return index !== -1
};

isTruncated = function (metadata) {
    if (metadata && metadata['$v']) {
        var parts = metadata['$v'].split(":");
        var projectionEpoch = parts[1];

        return (projectionEpoch < 0);
    }
    return false;
};
getStreamName = function (estateName) {
    return 'FileProcessorSubscriptionStream_' + estateName;
}

getStringWithNoSpaces = function (inputString) { return inputString.replace(/-/gi, "").replace(/ /g, ""); }

fromAll()
    .when({
        $init: function (s, e) {
            return { estates: {} }
        },
        $any: function (s, e) {
            if (isTruncated(e)) return;

            if (isEstateEvent(e)) {

                if (isAnEstateCreatedEvent(e)) {
                    s.estates[e.data.estateId] = {
                        filteredName: e.data.estateName.replace(/-/gi, ""),
                        name: getStringWithNoSpaces(e.data.estateName)
                    };
                }

                if (isARequiredEvent(e) === false) return;

                linkTo(getStreamName(s.estates[e.data.estateId].name), e);
            }
        }
    }
);