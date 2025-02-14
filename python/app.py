from random import randint
from flask import Flask, request
import logging
from pythonjsonlogger.json import JsonFormatter

app = Flask(__name__)
logging.basicConfig(level=logging.DEBUG)
logger = logging.getLogger(__name__)
logHandler = logging.StreamHandler()
formatter = JsonFormatter()
logHandler.setFormatter(formatter)
logger.addHandler(logHandler)


@app.route("/rolldice")
def roll_dice():
    player = request.args.get("player", default=None, type=str)
    result = str(roll())
    if player:
        logger.warning("rolling the dice", extra={"player": player, "result": result})
    else:
        logger.warning(
            "Anonymous player is rolling the dice: %s", extra={"result": result}
        )
    return result


def roll():
    return randint(1, 6)
