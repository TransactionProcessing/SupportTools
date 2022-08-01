//starttestsetup
var fromCategory = fromCategory || require('../../node_modules/@transactionprocessing/esprojection-testing-framework').scope.fromCategory;
var emit = emit || require('../../node_modules/@transactionprocessing/esprojection-testing-framework').scope.emit;
var linkTo = linkTo || require("../../node_modules/@transactionprocessing/esprojection-testing-framework").scope.linkTo;
//endtestsetup

fromCategory('TransactionAggregate')
    .foreachStream()
    .when({
        $any: function (s, e) {

            if (e === null || e.data === null || e.data.IsJson === false)
                return;

            eventbus.dispatch(s, e);
        }
    });

var eventbus = {
    dispatch: function (s, e) {

        if (e.eventType === 'MerchantFeeAddedToTransactionEvent') {
            merchantFeeAddedToTransactionEventHandler(s, e);
            return;
        }
        if (e.eventType === 'ServiceProviderFeeAddedToTransactionEvent') {
            serviceProviderFeeAddedToTransactionEventHandler(s, e);
            return;
        }
        else {
            //Just add the existing event to to our stream
            linkTo(getStreamName(s), e);
        }

    }
}

function merchantFeeAddedToTransactionEventHandler(s, e) {
    var newEvent = {
        calculatedValue: e.data.calculatedValue,
        feeCalculatedDateTime: e.data.feeCalculatedDateTime,
        estateId: e.data.estateId,
        feeId: e.data.feeId,
        feeValue: e.data.feeValue,
        merchantId: e.data.merchantId,
        transactionId: e.data.transactionId,
        feeCalculationType: e.data.feeCalculationType,
        eventId: e.eventId
    }
    emit(getStreamName(s), "MerchantFeeAddedToTransactionEnrichedEvent", newEvent, null);
}

function serviceProviderFeeAddedToTransactionEventHandler(s, e) {
    var newEvent = {
        calculatedValue: e.data.calculatedValue,
        feeCalculatedDateTime: e.data.feeCalculatedDateTime,
        estateId: e.data.estateId,
        feeId: e.data.feeId,
        feeValue: e.data.feeValue,
        merchantId: e.data.merchantId,
        transactionId: e.data.transactionId,
        feeCalculationType: e.data.feeCalculationType,
        eventId: e.eventId
    }
    emit(getStreamName(s), "ServiceProviderFeeAddedToTransactionEnrichedEvent", newEvent, null);
}

function getStreamName(s) {
    return "TransactionEnricherResult";
}