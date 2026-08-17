(function e(t,n,r){function s(o,u){if(!n[o]){if(!t[o]){var a=typeof require=="function"&&require;if(!u&&a)return a(o,!0);if(i)return i(o,!0);var f=new Error("Cannot find module '"+o+"'");throw f.code="MODULE_NOT_FOUND",f}var l=n[o]={exports:{}};t[o][0].call(l.exports,function(e){var n=t[o][1][e];return s(n?n:e)},l,l.exports,e,t,n,r)}return n[o].exports}var i=typeof require=="function"&&require;for(var o=0;o<r.length;o++)s(r[o]);return s})({1:[function(require,module,exports){
(function (__dirname){
"use strict";
var __awaiter = (this && this.__awaiter) || function (thisArg, _arguments, P, generator) {
    return new (P || (P = Promise))(function (resolve, reject) {
        function fulfilled(value) { try { step(generator.next(value)); } catch (e) { reject(e); } }
        function rejected(value) { try { step(generator["throw"](value)); } catch (e) { reject(e); } }
        function step(result) { result.done ? resolve(result.value) : new P(function (resolve) { resolve(result.value); }).then(fulfilled, rejected); }
        step((generator = generator.apply(thisArg, _arguments || [])).next());
    });
};
Object.defineProperty(exports, "__esModule", { value: true });
const fs = require("fs");
const path = require("path");
const jibo = require("jibo");
const log_1 = require("./log");
const root = jibo.utils.PathUtils.findRoot(__dirname);
const statsPath = path.join(root, 'diagnostics.log');
class BaseTest {
    constructor(testName) {
        this.logKey = undefined;
        this.logData = undefined;
        this._log = log_1.default.createChild(testName);
    }
    enter() {
        return __awaiter(this, void 0, void 0, function* () {
            this._log.info("enter");
            yield jibo.expression.centerRobot({ centerGlobally: true });
        });
    }
    exit() {
        this._log.info("exit");
    }
    update() {
    }
    click(event) {
        this._log.info("[tap event]", event.screenX, event.screenY);
    }
    readPersistentData() {
        if (!this.logKey || (!fs.existsSync(statsPath))) {
            return;
        }
        try {
            let data = JSON.parse(fs.readFileSync(statsPath, 'utf8'));
            if (data.hasOwnProperty(this.logKey)) {
                this.logData = data[this.logKey];
            }
        }
        catch (error) {
            this._log.error('\nError parsing \'' + statsPath + '\'. ' + error + '\n');
        }
    }
    writePersistentData() {
        if (!this.logKey) {
            return;
        }
        let data = {};
        if (fs.existsSync(statsPath)) {
            data = JSON.parse(fs.readFileSync(statsPath, 'utf8'));
        }
        data[this.logKey] = ((this.logData !== undefined) ? this.logData : {});
        try {
            fs.writeFileSync(statsPath, JSON.stringify(data, null, '    '), 'utf8');
        }
        catch (error) {
            this._log.error('\nError writing \'' + statsPath + '\'. ' + error + '\n');
        }
    }
}
exports.default = BaseTest;

}).call(this,"/src")

},{"./log":9,"fs":undefined,"jibo":undefined,"path":undefined}],2:[function(require,module,exports){
"use strict";
Object.defineProperty(exports, "__esModule", { value: true });
const jibo = require("jibo");
const querySelector_1 = require("./utils/querySelector");
const tests_1 = require("./tests");
const log_1 = require("./log");
class DiagnosticsSkill {
    constructor() {
        this._update = this.update.bind(this);
        querySelector_1.default('#touch-screen').addEventListener('mousedown', (event) => {
            this.clickScreen(event);
        });
        window.onbeforeunload = () => {
            this.stopCurrentTest();
        };
    }
    start() {
        this._enableShutdownWarning(false, '');
        const button = querySelector_1.default('#menu-button');
        button.onmousedown = (event) => {
            log_1.default.info("[tap event] menu button", event.screenX, event.screenY);
            const selector = querySelector_1.default('#select-menu');
            if (selector.style.display === 'none') {
                selector.style.display = 'block';
            }
            else {
                selector.style.display = 'none';
            }
        };
        const selector = querySelector_1.default('#select-menu');
        selector.style.display = 'none';
        selector.onclick = (event) => {
            log_1.default.info("[tap event] select menu", event.screenX, event.screenY);
            selector.style.display = 'none';
            const target = this.getEventTarget(event);
            this.selectTest(target);
            event.stopPropagation();
        };
        const first = selector.firstElementChild;
        this.selectTest(first);
    }
    getEventTarget(e) {
        e = e || window.event;
        return e.target || e.srcElement;
    }
    _enableShutdownWarning(enable, msg) {
        const notification = querySelector_1.default('#warning-label');
        notification.style.display = (enable ? 'initial' : 'none');
        notification.innerHTML = msg;
    }
    selectTest(element) {
        const tests = querySelector_1.default('#select-menu').getElementsByTagName('li');
        const len = tests.length;
        for (let i = 0; i < len; i++) {
            tests[i].style.backgroundColor = 'lightgray';
        }
        element.style.backgroundColor = 'gold';
        this.startTest(element.getAttribute('value'));
    }
    createTest(testName) {
        const testClass = tests_1.default[testName];
        return new testClass(testName);
    }
    startTest(testName) {
        this.stopCurrentTest();
        this.currentTest = this.createTest(testName);
        if (this.currentTest) {
            this.currentTest.readPersistentData();
            this.currentTest.enter();
            jibo.timer.on('update', this._update);
        }
    }
    update() {
        if (this.currentTest) {
            this.currentTest.update();
        }
    }
    stopCurrentTest() {
        if (this.currentTest) {
            jibo.timer.off('update', this._update);
            this.currentTest.writePersistentData();
            this.currentTest.exit();
        }
    }
    clickScreen(event) {
        if (this.currentTest) {
            this.currentTest.click(event);
        }
    }
}
exports.default = DiagnosticsSkill;

},{"./log":9,"./tests":23,"./utils/querySelector":24,"jibo":undefined}],3:[function(require,module,exports){
'use strict';
module.exports = function (blackboard, notepad, result, emitter) {
    return {
        '1': {
            'id': '1',
            'class': 'Sequence',
            'children': [
                12,
                2,
                26,
                23,
                36
            ],
            'decorators': [],
            'options': {}
        },
        '2': function () {
            return {
                'id': '2',
                'parent': 1,
                'asset-pack': 'core',
                'class': 'ListenEmbedded',
                'options': {
                    'rule': 'hey_jibo',
                    'onResult': listener => {
                        listener.on('hey-jibo', function (asrResult, speakerIds) {
                            notepad.speechResult = true;
                            blackboard.setLabel('heard you say hey jibo');
                        });
                    }
                }
            };
        },
        '12': function () {
            return {
                'id': '12',
                'parent': 1,
                'asset-pack': 'core',
                'class': 'ExecuteScript',
                'options': {
                    'exec': () => {
                        blackboard.setLabel('Say hey jibo');
                        notepad.speechResult = false;
                    }
                }
            };
        },
        '23': function () {
            return {
                'id': '23',
                'parent': 1,
                'asset-pack': 'core',
                'class': 'ExecuteScript',
                'options': {
                    'exec': () => {
                        blackboard.setLabel('Now say whatever you want');
                        notepad.textIndependent = false;
                    }
                }
            };
        },
        '26': function () {
            return {
                'id': '26',
                'parent': 1,
                'asset-pack': 'core',
                'class': 'TimeoutJs',
                'options': {
                    'getTime': () => {
                        return 1000;
                    }
                }
            };
        },
        '27': function () {
            return {
                'id': '27',
                'parent': 29,
                'asset-pack': 'core',
                'class': 'ExecuteScript',
                'decorators': [28],
                'options': {
                    'exec': () => {
                        blackboard.setLabel('didn\'t hear anything for text-independent speech');
                    }
                }
            };
        },
        '28': function () {
            return {
                'id': '28',
                'asset-pack': 'core',
                'class': 'Case',
                'options': {
                    'conditional': () => {
                        return !notepad.NLresult;
                    }
                }
            };
        },
        '29': {
            'id': '29',
            'parent': 36,
            'asset-pack': 'core',
            'class': 'Switch',
            'children': [
                27,
                38
            ],
            'options': {}
        },
        '36': {
            'id': '36',
            'parent': 1,
            'asset-pack': 'core',
            'class': 'Sequence',
            'children': [
                39,
                29
            ],
            'decorators': [37],
            'options': {}
        },
        '37': function () {
            return {
                'id': '37',
                'asset-pack': 'core',
                'class': 'WhileCondition',
                'options': {
                    'init': () => {
                    },
                    'conditional': () => {
                        return true;
                    }
                }
            };
        },
        '38': function () {
            return {
                'id': '38',
                'parent': 29,
                'asset-pack': 'core',
                'class': 'ExecuteScript',
                'options': {
                    'exec': () => {
                        notepad.NLresult = false;
                    }
                }
            };
        },
        '39': function () {
            return {
                'id': '39',
                'parent': 36,
                'asset-pack': 'core',
                'class': 'ListenJs',
                'options': {
                    'getOptions': () => {
                        let options = {
                            heyJibo: false,
                            detectEnd: true,
                            returnSpeakers: false,
                            incremental: true,
                            timeout: 6000,
                            bargein: false,
                            speakerName: ''
                        };
                        return options;
                    },
                    'getRule': callback => {
                        callback('TopGrammar = $* Hello World $*;');
                    },
                    'onResult': listener => {
                        listener.on('cloud', function (asrResult, speakerIds) {
                            blackboard.setLabel(asrResult.Input);
                            notepad.NLresult = true;
                        });
                    }
                }
            };
        },
        'meta': { 'version': 1 }
    };
};
},{}],4:[function(require,module,exports){
'use strict';
module.exports = function (blackboard, notepad, result, emitter) {
    return {
        '1': {
            'id': '1',
            'class': 'Sequence',
            'children': [
                5,
                6
            ],
            'decorators': [],
            'options': {}
        },
        '5': function () {
            return {
                'id': '5',
                'parent': 1,
                'asset-pack': 'core',
                'class': 'ExecuteScript',
                'options': {
                    'exec': () => {
                        notepad.position = {
                            x: 0.5,
                            y: 0.1,
                            z: 0.3
                        };
                    }
                }
            };
        },
        '6': {
            'id': '6',
            'parent': 1,
            'class': 'Sequence',
            'children': [
                7,
                9
            ],
            'decorators': [8],
            'options': {}
        },
        '7': function () {
            return {
                'id': '7',
                'name': 'follow my voice',
                'parent': 6,
                'asset-pack': 'core',
                'class': 'LookAt',
                'options': {
                    'getTarget': () => {
                        let jibo = require('jibo');
                        let entity = jibo.lps.getClosestAudibleEntity();
                        let valid = entity !== undefined && entity.confidence >= 0.2;
                        if (valid) {
                            let pos = entity.position;
                            let THREE = require('@jibo/three');
                            pos = new THREE.Vector3(pos.x, pos.y, pos.z);
                            pos.normalize();
                            let newZ = pos.z;
                            if (newZ > 0.5) {
                                newZ = 0.5;
                            }
                            if (newZ < 0.2) {
                                newZ = 0.2;
                            }
                            notepad.position = {
                                x: pos.x,
                                y: pos.y,
                                z: newZ
                            };
                            document.getElementById('test-label').innerHTML = `Heard you with ${ entity.confidence } at ${ notepad.position.x.toPrecision(3) }, ${ notepad.position.y.toPrecision(3) }, ${ notepad.position.z.toPrecision(3) }`;
                            return notepad.position;
                        } else {
                            let confid = undefined;
                            if (entity) {
                                confid = entity.confidence;
                            }
                            document.getElementById('test-label').innerHTML = `Conf=${ confid } Can't hear you :(`;
                            return notepad.position;
                        }
                    },
                    'isContinuous': false,
                    'config': lookAt => {
                    }
                }
            };
        },
        '8': function () {
            return {
                'id': '8',
                'asset-pack': 'core',
                'class': 'WhileCondition',
                'options': {
                    'init': () => {
                    },
                    'conditional': () => {
                        return true;
                    }
                }
            };
        },
        '9': function () {
            return {
                'id': '9',
                'name': 'wait a bit',
                'parent': 6,
                'asset-pack': 'core',
                'class': 'TimeoutJs',
                'options': {
                    'getTime': () => {
                        return 1000 + 1000 * Math.random();
                    }
                }
            };
        },
        'meta': { 'version': 1 }
    };
};
},{"@jibo/three":undefined,"jibo":undefined}],5:[function(require,module,exports){
'use strict';
module.exports = function (blackboard, notepad, result, emitter) {
    return {
        '1': {
            'id': '1',
            'class': 'Sequence',
            'children': [
                5,
                2,
                3,
                4,
                7,
                '0bb8471f-a335-4499-90ae-ffdfaac8eb54',
                '9060be94-52ce-46f0-9254-e7d44c1f2a16',
                '7989bfde-f8e9-44c0-b680-83dd87d5571e',
                6
            ],
            'options': {}
        },
        '2': function () {
            return {
                'id': '2',
                'name': 'head cycle',
                'parent': 1,
                'asset-pack': 'core',
                'class': 'PlayAnimation',
                'options': {
                    'animPath': 'spin_head.keys',
                    'config': builder => {
                        builder.setSpeed(notepad.animSpeed);
                    },
                    'animSelector': 1,
                    'cache': true,
                    'upload': true
                }
            };
        },
        '3': function () {
            return {
                'id': '3',
                'name': 'torso cycle',
                'parent': 1,
                'asset-pack': 'core',
                'class': 'PlayAnimation',
                'options': {
                    'animPath': 'spin_torso.keys',
                    'config': builder => {
                        builder.setSpeed(notepad.animSpeed);
                    },
                    'animSelector': 1,
                    'cache': true,
                    'upload': true
                }
            };
        },
        '4': function () {
            return {
                'id': '4',
                'name': 'pelvis cycle',
                'parent': 1,
                'asset-pack': 'core',
                'class': 'PlayAnimation',
                'options': {
                    'animPath': 'spin_pelvis.keys',
                    'config': builder => {
                        builder.setSpeed(notepad.animSpeed);
                    },
                    'animSelector': 1,
                    'cache': true,
                    'upload': true
                }
            };
        },
        '5': function () {
            return {
                'id': '5',
                'name': 'get random anim speed',
                'parent': 1,
                'asset-pack': 'core',
                'class': 'ExecuteScript',
                'options': {
                    'exec': () => {
                        notepad.animSpeed = parseFloat(Math.random() * (0.75 - 0.6) + 0.6).toFixed(2);
                        document.getElementById('test-label3').innerHTML = 'anim speed: ' + notepad.animSpeed * 100 + '%';
                    }
                }
            };
        },
        '6': function () {
            return {
                'id': '6',
                'name': 'rest ',
                'parent': 1,
                'asset-pack': 'core',
                'class': 'TimeoutJs',
                'options': {
                    'getTime': () => {
                        document.getElementById('test-label3').innerHTML = 'resting for 5 secs';
                        return 5000;
                    }
                }
            };
        },
        '7': function () {
            return {
                'id': '7',
                'name': 'twist everything',
                'parent': 1,
                'asset-pack': 'core',
                'class': 'PlayAnimation',
                'options': {
                    'animPath': 'twist.keys',
                    'config': animation => {
                    },
                    'animSelector': 1,
                    'cache': true,
                    'upload': true
                }
            };
        },
        'meta': { 'version': 1 },
        '9060be94-52ce-46f0-9254-e7d44c1f2a16': function () {
            return {
                'id': '9060be94-52ce-46f0-9254-e7d44c1f2a16',
                'name': 'index',
                'parent': 1,
                'asset-pack': 'core',
                'class': 'ExecuteScriptAsync',
                'options': {
                    'exec': (succeed, fail) => {
                        document.getElementById('test-label3').innerHTML = 'indexing...';
                        jibo.expression.indexRobot().then(() => {
                            succeed();
                        }).catch(error => {
                            console.error(error);
                            succeed();
                        });
                    }
                }
            };
        },
        '0bb8471f-a335-4499-90ae-ffdfaac8eb54': function () {
            return {
                'id': '0bb8471f-a335-4499-90ae-ffdfaac8eb54',
                'name': 'Center',
                'parent': 1,
                'asset-pack': 'core',
                'class': 'ExecuteScriptAsync',
                'options': {
                    'exec': (succeed, fail) => {
                        jibo.expression.centerRobot({ centerGlobally: true }).then(succeed);
                    }
                }
            };
        },
        '7989bfde-f8e9-44c0-b680-83dd87d5571e': function () {
            return {
                'id': '7989bfde-f8e9-44c0-b680-83dd87d5571e',
                'name': 'Center',
                'parent': 1,
                'asset-pack': 'core',
                'class': 'ExecuteScriptAsync',
                'options': {
                    'exec': (succeed, fail) => {
                        jibo.expression.centerRobot({ centerGlobally: true }).then(succeed);
                    }
                }
            };
        }
    };
};
},{}],6:[function(require,module,exports){
'use strict';
module.exports = function (blackboard, notepad, result, emitter) {
    return {
        '1': {
            'id': '1',
            'name': 'sequence of blinks and lookAts that will run an idle for ~4 mins',
            'asset-pack': 'core',
            'class': 'Sequence',
            'children': [
                5,
                7
            ],
            'options': {}
        },
        '3': {
            'id': '3',
            'name': 'look at random spots',
            'parent': 7,
            'asset-pack': 'core',
            'class': 'Sequence',
            'children': [
                8,
                134
            ],
            'decorators': [4],
            'options': {}
        },
        '4': function () {
            return {
                'id': '4',
                'asset-pack': 'core',
                'class': 'WhileCondition',
                'options': {
                    'init': () => {
                    },
                    'conditional': () => {
                        return notepad.running;
                    }
                }
            };
        },
        '5': function () {
            return {
                'id': '5',
                'name': 'initialize our variables',
                'parent': 1,
                'asset-pack': 'core',
                'class': 'ExecuteScript',
                'options': {
                    'exec': () => {
                        notepad.running = true;
                        notepad.resting = false;
                        notepad.cycleLength = 240;
                        notepad.hrStart = process.hrtime();
                        notepad.hrRestStart = 0;
                    }
                }
            };
        },
        '7': {
            'id': '7',
            'parent': 1,
            'asset-pack': 'core',
            'class': 'Parallel',
            'children': [
                38,
                21,
                10,
                23,
                3
            ],
            'options': { 'succeedOnOne': false }
        },
        '8': function () {
            return {
                'id': '8',
                'parent': 3,
                'asset-pack': 'core',
                'class': 'TimeoutJs',
                'options': {
                    'getTime': () => {
                        return 1500 + 1500 * Math.random();
                    }
                }
            };
        },
        '10': {
            'id': '10',
            'name': 'blink randomly',
            'parent': 7,
            'asset-pack': 'core',
            'class': 'Sequence',
            'children': [
                15,
                16
            ],
            'decorators': [11],
            'options': {}
        },
        '11': function () {
            return {
                'id': '11',
                'asset-pack': 'core',
                'class': 'WhileCondition',
                'options': {
                    'init': () => {
                    },
                    'conditional': () => {
                        return notepad.running;
                    }
                }
            };
        },
        '15': function () {
            return {
                'id': '15',
                'parent': 10,
                'asset-pack': 'core',
                'class': 'TimeoutJs',
                'options': {
                    'getTime': () => {
                        return 2000 + 1500 * Math.random();
                    }
                }
            };
        },
        '16': {
            'id': '16',
            'parent': 10,
            'asset-pack': 'core',
            'class': 'Blink',
            'options': {}
        },
        '17': function () {
            return {
                'id': '17',
                'parent': 134,
                'asset-pack': 'core',
                'class': 'LookAt',
                'options': {
                    'getTarget': () => {
                        let x = 1 - 2 * Math.random();
                        let y = 1 - 2 * Math.random();
                        let z = 0.7 + 0.5 * (1 - 2 * Math.random());
                        return {
                            x: x,
                            y: y,
                            z: z
                        };
                    }
                }
            };
        },
        '18': function () {
            return {
                'id': '18',
                'parent': 21,
                'asset-pack': 'core',
                'class': 'ExecuteScript',
                'decorators': [],
                'options': {
                    'exec': () => {
                        let timeDiff = process.hrtime(notepad.hrStart);
                        document.getElementById('test-label3').innerHTML = 'animating time: ' + timeDiff[0] + 's';
                        if (timeDiff[0] >= notepad.cycleLength) {
                            notepad.running = false;
                        }
                    }
                }
            };
        },
        '21': {
            'id': '21',
            'name': 'update timer',
            'parent': 7,
            'asset-pack': 'core',
            'class': 'Sequence',
            'children': [18],
            'decorators': [22],
            'options': {}
        },
        '22': function () {
            return {
                'id': '22',
                'asset-pack': 'core',
                'class': 'WhileCondition',
                'options': {
                    'init': () => {
                    },
                    'conditional': () => {
                        return notepad.running;
                    }
                }
            };
        },
        '23': {
            'id': '23',
            'name': 'color LED randomly',
            'parent': 7,
            'asset-pack': 'core',
            'class': 'Sequence',
            'children': [
                28,
                29
            ],
            'decorators': [24],
            'options': {}
        },
        '24': function () {
            return {
                'id': '24',
                'parent': 13,
                'asset-pack': 'core',
                'class': 'WhileCondition',
                'options': {
                    'init': () => {
                    },
                    'conditional': () => {
                        return notepad.running;
                    }
                }
            };
        },
        '28': function () {
            return {
                'id': '28',
                'parent': 23,
                'asset-pack': 'core',
                'class': 'TimeoutJs',
                'options': {
                    'getTime': () => {
                        return 1000 + 1000 * Math.random();
                    }
                }
            };
        },
        '29': function () {
            return {
                'id': '29',
                'parent': 23,
                'asset-pack': 'core',
                'class': 'ExecuteScript',
                'options': {
                    'exec': () => {
                        let r = Math.floor(Math.random() * 256) / 255;
                        let g = Math.floor(Math.random() * 256) / 255;
                        let b = Math.floor(Math.random() * 256) / 255;
                        jibo.expression.setLEDColor([
                            r,
                            g,
                            b
                        ]);
                    }
                }
            };
        },
        '38': {
            'id': '38',
            'name': 'resting',
            'parent': 7,
            'asset-pack': 'core',
            'class': 'Sequence',
            'children': [
                118,
                68
            ],
            'decorators': [67],
            'options': {}
        },
        '67': function () {
            return {
                'id': '67',
                'asset-pack': 'core',
                'class': 'StartOnCondition',
                'options': {
                    'init': () => {
                    },
                    'conditional': () => {
                        return !notepad.running && !notepad.resting;
                    }
                }
            };
        },
        '68': function () {
            return {
                'id': '68',
                'parent': 38,
                'asset-pack': 'core',
                'class': 'ExecuteScript',
                'decorators': [119],
                'options': {
                    'exec': () => {
                        let restTimeDiff = process.hrtime(notepad.hrRestStart);
                        document.getElementById('test-label3').innerHTML = 'resting time: ' + restTimeDiff[0] + 's';
                        if (restTimeDiff[0] >= notepad.cycleLength) {
                            notepad.resting = false;
                        }
                    }
                }
            };
        },
        '118': function () {
            return {
                'id': '118',
                'parent': 38,
                'asset-pack': 'core',
                'class': 'ExecuteScript',
                'options': {
                    'exec': () => {
                        notepad.resting = true;
                        notepad.hrRestStart = process.hrtime();
                    }
                }
            };
        },
        '119': function () {
            return {
                'id': '119',
                'asset-pack': 'core',
                'class': 'WhileCondition',
                'options': {
                    'init': () => {
                    },
                    'conditional': () => {
                        return notepad.resting;
                    }
                }
            };
        },
        '120': {
            'id': '120',
            'parent': 128,
            'asset-pack': 'core',
            'class': 'PlayAudio',
            'options': { 'audioPath': 'FX_Bleep.mp3' }
        },
        '124': {
            'id': '124',
            'parent': 128,
            'asset-pack': 'core',
            'class': 'PlayAudio',
            'options': { 'audioPath': 'FX_Blip.mp3' }
        },
        '128': {
            'id': '128',
            'parent': 134,
            'asset-pack': 'core',
            'class': 'Random',
            'children': [
                124,
                129,
                120
            ],
            'options': {}
        },
        '129': {
            'id': '129',
            'parent': 128,
            'asset-pack': 'core',
            'class': 'PlayAudio',
            'options': { 'audioPath': 'FX_Bloop.mp3' }
        },
        '134': {
            'id': '134',
            'parent': 3,
            'asset-pack': 'core',
            'class': 'Sequence',
            'children': [
                128,
                17
            ],
            'options': {}
        },
        'meta': { 'version': 1 }
    };
};
},{}],7:[function(require,module,exports){
'use strict';
module.exports = function (blackboard, notepad, result, emitter) {
    return {
        '1': {
            'id': '1',
            'class': 'Sequence',
            'children': [11],
            'decorators': [],
            'options': {}
        },
        '11': function () {
            return {
                'id': '11',
                'name': 'follow me with a continuous look at!',
                'parent': 1,
                'asset-pack': 'core',
                'class': 'LookAt',
                'options': {
                    'getTarget': () => {
                        let jibo = require('jibo');
                        let entity = jibo.lps.getClosestVisualEntity();
                        let position = {
                            x: 0.5,
                            y: 0.1,
                            z: 0.4
                        };
                        if (entity) {
                            document.getElementById('test-label').innerHTML = 'Found you :)';
                            position = {
                                x: entity.position.x,
                                y: entity.position.y,
                                z: entity.position.z
                            };
                            document.getElementById('test-label').innerHTML = `Found you at ${ position.x.toPrecision(3) }, ${ position.y.toPrecision(3) }, ${ position.z.toPrecision(3) }`;
                        } else {
                            document.getElementById('test-label').innerHTML = 'Lost you :(';
                        }
                        return position;
                    },
                    'isContinuous': true,
                    'config': lookAt => {
                    }
                }
            };
        },
        'meta': { 'version': 1 }
    };
};
},{"jibo":undefined}],8:[function(require,module,exports){
"use strict";
Object.defineProperty(exports, "__esModule", { value: true });
const jibo = require("jibo");
require("./utils/stats");
const DiagnosticsSkill_1 = require("./DiagnosticsSkill");
const log_1 = require("./log");
const skill = new DiagnosticsSkill_1.default();
jibo.init({ display: 'face' }, () => {
    log_1.loadLogConfig();
    jibo.action.configure({ orientToHJ: false });
    jibo.expression.setAttentionMode(jibo.expression.AttentionMode.OFF)
        .then(() => {
        return jibo.expression.indexRobot();
    })
        .then(() => {
        skill.start();
    })
        .catch((error) => {
        log_1.default.error(error);
        skill.start();
    });
});

},{"./DiagnosticsSkill":2,"./log":9,"./utils/stats":25,"jibo":undefined}],9:[function(require,module,exports){
"use strict";
Object.defineProperty(exports, "__esModule", { value: true });
const jibo = require("jibo");
const path = require("path");
const fs = require("fs");
const jibo_log_1 = require("jibo-log");
jibo_log_1.Log.processName = "jibo-diagnostics";
const log = new jibo_log_1.Log('Diagnostics');
exports.default = log;
function loadLogConfig() {
    let configPath = path.join(jibo.utils.PathUtils.findRoot(), 'logging.json');
    if (fs.existsSync(configPath)) {
        try {
            jibo_log_1.Log.loadConfig(JSON.parse(fs.readFileSync(configPath, 'utf-8')));
            log.info(`Loaded log configuration from '${configPath}'`);
        }
        catch (err) {
            console.error(`Error parsing logging config file '${configPath}': ${err.message}`);
        }
    }
    else {
        console.error(`No logging configuration found at '${configPath}'`);
    }
}
exports.loadLogConfig = loadLogConfig;

},{"fs":undefined,"jibo":undefined,"jibo-log":undefined,"path":undefined}],10:[function(require,module,exports){
"use strict";
var __awaiter = (this && this.__awaiter) || function (thisArg, _arguments, P, generator) {
    return new (P || (P = Promise))(function (resolve, reject) {
        function fulfilled(value) { try { step(generator.next(value)); } catch (e) { reject(e); } }
        function rejected(value) { try { step(generator["throw"](value)); } catch (e) { reject(e); } }
        function step(result) { result.done ? resolve(result.value) : new P(function (resolve) { resolve(result.value); }).then(fulfilled, rejected); }
        step((generator = generator.apply(thisArg, _arguments || [])).next());
    });
};
Object.defineProperty(exports, "__esModule", { value: true });
const jibo = require("jibo");
const BaseTest_1 = require("../BaseTest");
const querySelector_1 = require("../utils/querySelector");
const asrTest = require('../behaviors/asr-test');
class ASRTest extends BaseTest_1.default {
    constructor(testName) {
        super(testName);
        const blackboard = {
            setLabel: this.setLabel
        };
        this.root = jibo.bt.create(asrTest, { blackboard: blackboard });
    }
    setLabel(label) {
        querySelector_1.default('#test-label').innerHTML = label;
    }
    enter() {
        const _super = name => super[name];
        return __awaiter(this, void 0, void 0, function* () {
            _super("enter").call(this);
            this.root.start();
        });
    }
    exit() {
        super.exit();
        this.root.stop();
        querySelector_1.default('#test-label').innerHTML = '';
    }
    update() {
        this.root.update();
    }
}
exports.default = ASRTest;

},{"../BaseTest":1,"../behaviors/asr-test":3,"../utils/querySelector":24,"jibo":undefined}],11:[function(require,module,exports){
"use strict";
var __awaiter = (this && this.__awaiter) || function (thisArg, _arguments, P, generator) {
    return new (P || (P = Promise))(function (resolve, reject) {
        function fulfilled(value) { try { step(generator.next(value)); } catch (e) { reject(e); } }
        function rejected(value) { try { step(generator["throw"](value)); } catch (e) { reject(e); } }
        function step(result) { result.done ? resolve(result.value) : new P(function (resolve) { resolve(result.value); }).then(fulfilled, rejected); }
        step((generator = generator.apply(thisArg, _arguments || [])).next());
    });
};
Object.defineProperty(exports, "__esModule", { value: true });
const jibo = require("jibo");
const BaseTest_1 = require("../BaseTest");
const querySelector_1 = require("../utils/querySelector");
const audioContext = new AudioContext();
let buffer = null;
class AudioTest extends BaseTest_1.default {
    constructor(testName) {
        super(testName);
        this.bufferSource = null;
        const request = new XMLHttpRequest();
        const url = jibo.utils.PathUtils.getAssetUri('audio/FX_Bleep.mp3');
        request.open('GET', url, true);
        request.responseType = 'arraybuffer';
        request.onload = () => {
            audioContext.decodeAudioData(request.response, (_buffer) => {
                buffer = _buffer;
            }, (error) => {
                throw new Error('Error loading sound file: ' + url);
            });
        };
        request.send();
    }
    enter() {
        const _super = name => super[name];
        return __awaiter(this, void 0, void 0, function* () {
            _super("enter").call(this);
            querySelector_1.default('#test-label').innerHTML = 'Click to play';
        });
    }
    exit() {
        super.exit();
        this._stop();
        querySelector_1.default('#test-label').innerHTML = '';
    }
    click(event) {
        super.click(event);
        this._stop();
        this._play();
    }
    _stop() {
        if (!this.bufferSource) {
            return;
        }
        this.bufferSource.disconnect();
        this.bufferSource = null;
    }
    _play() {
        if (!buffer) {
            this._log.info('Audio buffer not loaded yet...');
            return;
        }
        this.bufferSource = audioContext.createBufferSource();
        this.bufferSource.buffer = buffer;
        this.bufferSource.connect(audioContext.destination);
        this.bufferSource.start(0);
    }
}
exports.default = AudioTest;

},{"../BaseTest":1,"../utils/querySelector":24,"jibo":undefined}],12:[function(require,module,exports){
"use strict";
var __awaiter = (this && this.__awaiter) || function (thisArg, _arguments, P, generator) {
    return new (P || (P = Promise))(function (resolve, reject) {
        function fulfilled(value) { try { step(generator.next(value)); } catch (e) { reject(e); } }
        function rejected(value) { try { step(generator["throw"](value)); } catch (e) { reject(e); } }
        function step(result) { result.done ? resolve(result.value) : new P(function (resolve) { resolve(result.value); }).then(fulfilled, rejected); }
        step((generator = generator.apply(thisArg, _arguments || [])).next());
    });
};
Object.defineProperty(exports, "__esModule", { value: true });
const BaseTest_1 = require("../BaseTest");
const jibo = require("jibo");
const querySelector_1 = require("../utils/querySelector");
const audioTracking = require('../behaviors/audio-tracking');
class AudioTrackingTest extends BaseTest_1.default {
    constructor(testName) {
        super(testName);
        this.root = jibo.bt.create(audioTracking);
    }
    enter() {
        const _super = name => super[name];
        return __awaiter(this, void 0, void 0, function* () {
            _super("enter").call(this);
            querySelector_1.default('#test-label').innerHTML = 'Say something and I\'ll follow your voice!';
            this.root.start();
        });
    }
    exit() {
        super.exit();
        querySelector_1.default('#test-label').innerHTML = '';
        this.root.stop();
    }
    update() {
        this.root.update();
    }
}
exports.default = AudioTrackingTest;

},{"../BaseTest":1,"../behaviors/audio-tracking":4,"../utils/querySelector":24,"jibo":undefined}],13:[function(require,module,exports){
"use strict";
var __awaiter = (this && this.__awaiter) || function (thisArg, _arguments, P, generator) {
    return new (P || (P = Promise))(function (resolve, reject) {
        function fulfilled(value) { try { step(generator.next(value)); } catch (e) { reject(e); } }
        function rejected(value) { try { step(generator["throw"](value)); } catch (e) { reject(e); } }
        function step(result) { result.done ? resolve(result.value) : new P(function (resolve) { resolve(result.value); }).then(fulfilled, rejected); }
        step((generator = generator.apply(thisArg, _arguments || [])).next());
    });
};
Object.defineProperty(exports, "__esModule", { value: true });
const jibo = require("jibo");
const moment = require("moment");
const BaseTest_1 = require("../BaseTest");
const querySelector_1 = require("../utils/querySelector");
const body = require('../behaviors/body');
const Status = jibo.bt.Status;
class BodyTest extends BaseTest_1.default {
    constructor(testName) {
        super(testName);
        this.logKey = 'bodytest';
        this.root = jibo.bt.create(body);
        this.exiting = false;
        this.dirty = true;
        this.cyclesSoFar = 0;
    }
    _updateLogData() {
        this.logData['first'] = {
            'count': this.cyclesSoFar,
            'time': moment().format()
        };
        this.writePersistentData();
    }
    enter() {
        const _super = name => super[name];
        return __awaiter(this, void 0, void 0, function* () {
            _super("enter").call(this);
            if (this.logData !== undefined) {
                if (this.logData.hasOwnProperty('second')) {
                    this.logData['third'] = this.logData['second'];
                }
                if (this.logData.hasOwnProperty('first')) {
                    this.logData['second'] = this.logData['first'];
                }
            }
            else {
                this.logData = {};
            }
            this._updateLogData();
            let div = querySelector_1.default('#misc-log');
            div.style.display = 'block';
            div.innerHTML += '<h4>Previous Runs</h4>';
            let last1 = 'N/A';
            let last2 = 'N/A';
            if (this.logData.hasOwnProperty('second')) {
                last1 = this.logData['second'].time + ': ' + this.logData['second'].count;
                if (this.logData.hasOwnProperty('third')) {
                    last2 = this.logData['third'].time + ': ' + this.logData['third'].count;
                }
            }
            div.innerHTML += '<span class="item">' + last1 + '</span>';
            div.innerHTML += '<span class="item">' + last2 + '</span>';
            this._startCycle();
        });
    }
    _startCycle() {
        this.root.start();
        this.dirty = true;
    }
    _isPaused() {
        return (this.root && (this.root.currentStatus === Status.PAUSED));
    }
    exit() {
        super.exit();
        this.exiting = true;
        querySelector_1.default('#test-label').innerHTML = '';
        querySelector_1.default('#test-label2').innerHTML = '';
        querySelector_1.default('#test-label3').innerHTML = '';
        let div = querySelector_1.default('#misc-log');
        div.style.display = 'none';
        div.innerHTML = '';
        this._exitHelper();
    }
    _exitHelper() {
        if (this.root) {
            this.root.stop();
            this.root = undefined;
        }
    }
    update() {
        if (this.exiting) {
            return;
        }
        this.root.update();
        if (this.root.currentStatus === Status.SUCCEEDED) {
            querySelector_1.default('#test-label3').innerHTML = '';
            this.cyclesSoFar++;
            this._updateLogData();
            this._startCycle();
            this.dirty = true;
        }
        if (this.dirty) {
            querySelector_1.default('#test-label').innerHTML = (this._isPaused() ? 'Paused (tap to toggle)' : 'Not Paused (tap to toggle)');
            querySelector_1.default('#test-label2').innerHTML = 'cycles completed: ' + this.cyclesSoFar;
        }
    }
    click(event) {
        super.click(event);
        if (this._isPaused()) {
            this.root.unpause();
        }
        else {
            this.root.pause();
        }
    }
}
exports.default = BodyTest;

},{"../BaseTest":1,"../behaviors/body":5,"../utils/querySelector":24,"jibo":undefined,"moment":undefined}],14:[function(require,module,exports){
"use strict";
var __awaiter = (this && this.__awaiter) || function (thisArg, _arguments, P, generator) {
    return new (P || (P = Promise))(function (resolve, reject) {
        function fulfilled(value) { try { step(generator.next(value)); } catch (e) { reject(e); } }
        function rejected(value) { try { step(generator["throw"](value)); } catch (e) { reject(e); } }
        function step(result) { result.done ? resolve(result.value) : new P(function (resolve) { resolve(result.value); }).then(fulfilled, rejected); }
        step((generator = generator.apply(thisArg, _arguments || [])).next());
    });
};
Object.defineProperty(exports, "__esModule", { value: true });
const BaseTest_1 = require("../BaseTest");
const jibo = require("jibo");
const querySelector_1 = require("../utils/querySelector");
const jibo_cai_utils_1 = require("jibo-cai-utils");
let colors = [
    [1, 1, 1],
    [1, 0, 0],
    [1, 77 / 255, 0],
    [1, 1, 0],
    [72 / 255, 1, 0],
    [0, 234 / 255, 1],
    [0, 17 / 255, 1],
    [98 / 255, 0, 1],
    [1, 0, 1],
    [1, 1, 1]
];
class LEDTest extends BaseTest_1.default {
    constructor(testName) {
        super(testName);
        this.exiting = false;
    }
    enter() {
        const _super = name => super[name];
        return __awaiter(this, void 0, void 0, function* () {
            _super("enter").call(this);
            yield jibo.expression.blink();
            yield jibo.expression.centerRobot({ centerGlobally: true });
            querySelector_1.default('#test-label').innerHTML = 'Pretty colors';
            this.exiting = false;
            this.laodAndPlay();
        });
    }
    laodAndPlay() {
        return __awaiter(this, void 0, void 0, function* () {
            for (let i = 0; i < colors.length; i++) {
                jibo.expression.setLEDColor(colors[i]);
                yield jibo_cai_utils_1.PromiseUtils.promisify(cb => setTimeout(cb, 500));
            }
            if (!this.exiting) {
                this.laodAndPlay();
            }
        });
    }
    exit() {
        super.exit();
        querySelector_1.default('#test-label').innerHTML = '';
        this.exiting = true;
        jibo.expression.setLEDColor([0, 0, 0]);
    }
}
exports.default = LEDTest;

},{"../BaseTest":1,"../utils/querySelector":24,"jibo":undefined,"jibo-cai-utils":undefined}],15:[function(require,module,exports){
"use strict";
var __awaiter = (this && this.__awaiter) || function (thisArg, _arguments, P, generator) {
    return new (P || (P = Promise))(function (resolve, reject) {
        function fulfilled(value) { try { step(generator.next(value)); } catch (e) { reject(e); } }
        function rejected(value) { try { step(generator["throw"](value)); } catch (e) { reject(e); } }
        function step(result) { result.done ? resolve(result.value) : new P(function (resolve) { resolve(result.value); }).then(fulfilled, rejected); }
        step((generator = generator.apply(thisArg, _arguments || [])).next());
    });
};
Object.defineProperty(exports, "__esModule", { value: true });
const BaseTest_1 = require("../BaseTest");
const jibo = require("jibo");
const querySelector_1 = require("../utils/querySelector");
const lifecycle = require('../behaviors/lifecycle');
const Status = jibo.bt.Status;
class LifeCycleTest extends BaseTest_1.default {
    constructor(testName) {
        super(testName);
        this.root = jibo.bt.create(lifecycle);
        this.exiting = false;
        this.cyclesSoFar = 0;
        this.looping = true;
        this.dirty = false;
    }
    enter() {
        const _super = name => super[name];
        return __awaiter(this, void 0, void 0, function* () {
            _super("enter").call(this);
            this._startCycle();
        });
    }
    _startCycle() {
        const phrases = [
            'What\'s up',
            'Yo',
            'Howdy',
            'Hi ya',
            'Greetings',
            'Hello',
            'Dude, that\'s rad',
            'Sup bro',
            'Hey guys and gals',
            'Danger! Danger, Will Robinson!'
        ];
        const randomInt = Math.floor(Math.random() * phrases.length);
        const phrase = phrases[randomInt];
        jibo.tts.speak(phrase, function (err) {
            if (err) {
                this._log.error('tts.speak() error: ' + err);
            }
        });
        this.root.start();
        this.dirty = true;
    }
    exit() {
        super.exit();
        querySelector_1.default('#test-label').innerHTML = '';
        querySelector_1.default('#test-label2').innerHTML = '';
        querySelector_1.default('#test-label3').innerHTML = '';
        this.exiting = true;
        this._exitHelper();
    }
    _exitHelper() {
        if (this.root) {
            this.root.stop();
            this.root = undefined;
        }
        jibo.expression.setLEDColor([0, 0, 0]);
    }
    click(event) {
        super.click(event);
        this.looping = !this.looping;
        this.dirty = true;
    }
    update() {
        if (this.exiting) {
            return;
        }
        this.root.update();
        if (this.root.currentStatus === Status.SUCCEEDED) {
            this.root.pause();
            this.cyclesSoFar++;
            this.dirty = true;
        }
        if (this.looping && (this.root.currentStatus === Status.PAUSED)) {
            this._startCycle();
        }
        if (this.dirty) {
            querySelector_1.default('#test-label').innerHTML = (this.looping ? 'Looping (tap to toggle)' : 'Not looping (tap to toggle)');
            querySelector_1.default('#test-label2').innerHTML = 'cycles completed: ' + this.cyclesSoFar;
            this.dirty = false;
        }
    }
}
exports.default = LifeCycleTest;

},{"../BaseTest":1,"../behaviors/lifecycle":6,"../utils/querySelector":24,"jibo":undefined}],16:[function(require,module,exports){
"use strict";
var __awaiter = (this && this.__awaiter) || function (thisArg, _arguments, P, generator) {
    return new (P || (P = Promise))(function (resolve, reject) {
        function fulfilled(value) { try { step(generator.next(value)); } catch (e) { reject(e); } }
        function rejected(value) { try { step(generator["throw"](value)); } catch (e) { reject(e); } }
        function step(result) { result.done ? resolve(result.value) : new P(function (resolve) { resolve(result.value); }).then(fulfilled, rejected); }
        step((generator = generator.apply(thisArg, _arguments || [])).next());
    });
};
Object.defineProperty(exports, "__esModule", { value: true });
const BaseTest_1 = require("../BaseTest");
const querySelector_1 = require("../utils/querySelector");
class MathTest extends BaseTest_1.default {
    constructor(testName) {
        super(testName);
    }
    enter() {
        const _super = name => super[name];
        return __awaiter(this, void 0, void 0, function* () {
            _super("enter").call(this);
            const tests = {
                'abs': ['(-99)', 99],
                'acos': ['(-1)', 3.141592653589793],
                'acosh': ['(2)', 1.3169578969248166],
                'asin': ['(1)', 1.5707963267948966],
                'asinh': ['(1)', 0.8813735870195429],
                'atan': ['(1)', 0.7853981633974483],
                'atan2': ['(0,0)', 0],
                'atanh': ['(0.5)', 0.5493061443340549],
                'cbrt': ['(-1)', -1],
                'ceil': ['(0.95)', 1],
                'clz32': ['(1)', 31],
                'cos': ['(Math.PI)', -1],
                'cosh': ['(1)', 1.5430806348152437],
                'exp': ['(-1)', 0.3678794411714424],
                'expm1': ['(-1)', -0.6321205588285577],
                'floor': ['(3.5)', 3],
                'fround': ['(1.337)', 1.3370000123977661],
                'hypot': ['(3, 4)', 5],
                'imul': ['(2, 4)', 8],
                'log': ['(10)', 2.302585092994046],
                'log10': ['(100000)', 5],
                'log1p': ['(1)', 0.6931471805599453],
                'log2': ['(1024)', 10],
                'max': ['(1,2)', 2],
                'min': ['(1,2)', 1],
                'pow': ['(7, 2)', 49],
                'round': ['(20.49)', 20],
                'sign': ['(-3)', -1],
                'sin': ['(Math.PI/2)', 1],
                'sinh': ['(1)', 1.1752011936438014],
                'sqrt': ['(4)', 2],
                'tan': ['(1)', 1.5574077246549023],
                'tanh': ['(1)', 0.7615941559557649],
                'trunc': ['(13.37)', 13],
                'E': ['', 2.718281828459045],
                'LN2': ['', 0.6931471805599453],
                'LN10': ['', 2.302585092994046],
                'LOG2E': ['', 1.4426950408889634],
                'LOG10E': ['', 0.4342944819032518],
                'PI': ['', 3.141592653589793],
                'SQRT1_2': ['', 0.7071067811865476],
                'SQRT2': ['', 1.4142135623730951]
            };
            let successful = '', errors = '';
            for (let name in tests) {
                let answer = eval('Math.' + name + tests[name][0]);
                if (Math.abs(answer - tests[name][1]) < Number.EPSILON) {
                    successful += '<span class="left"><a href="https://developer.mozilla.org/en-US/docs/Web/JavaScript/Reference/Global_Objects/Math/' + name + '">Math.' + name + tests[name][0] + '</a> = ' + answer + '</span><span class="right">' + tests[name][1] + '</span><div style="clear:both"></div>';
                }
                else {
                    errors += '<span class="left"><span class="error"><a href="https://developer.mozilla.org/en-US/docs/Web/JavaScript/Reference/Global_Objects/Math/' + name + '">Math.' + name + tests[name][0] + '</a> = ' + answer + '</span></span><span class="right">' + tests[name][1] + '</span></span><div style="clear:both"></div>';
                }
            }
            const mathDiv = querySelector_1.default('#math-results');
            mathDiv.style.display = 'block';
            mathDiv.innerHTML = '<span class="left"><h2>Tests</h2></span><span class="right"><h2>Should be</h2></span><p></p><p></p><p></p><div style="clear:both"></div>';
            mathDiv.innerHTML += errors + successful;
            document.body.style.overflowX = 'hidden';
            document.body.style.overflowY = 'scroll';
        });
    }
    exit() {
        super.exit();
        const mathDiv = querySelector_1.default('#math-results');
        mathDiv.style.display = 'none';
        mathDiv.innerHTML = '';
        document.body.style.overflowY = 'hidden';
    }
    update() {
    }
}
exports.default = MathTest;

},{"../BaseTest":1,"../utils/querySelector":24}],17:[function(require,module,exports){
"use strict";
var __awaiter = (this && this.__awaiter) || function (thisArg, _arguments, P, generator) {
    return new (P || (P = Promise))(function (resolve, reject) {
        function fulfilled(value) { try { step(generator.next(value)); } catch (e) { reject(e); } }
        function rejected(value) { try { step(generator["throw"](value)); } catch (e) { reject(e); } }
        function step(result) { result.done ? resolve(result.value) : new P(function (resolve) { resolve(result.value); }).then(fulfilled, rejected); }
        step((generator = generator.apply(thisArg, _arguments || [])).next());
    });
};
Object.defineProperty(exports, "__esModule", { value: true });
const BaseTest_1 = require("../BaseTest");
const jibo = require("jibo");
const path = require("path");
const querySelector_1 = require("../utils/querySelector");
class NoTest extends BaseTest_1.default {
    constructor(testName) {
        super(testName);
        this.exiting = false;
        this.bodyConnected = true;
    }
    enter() {
        const _super = name => super[name];
        return __awaiter(this, void 0, void 0, function* () {
            _super("enter").call(this);
            jibo.systemManager.getDisplayVersion(function (error, version) {
                if (!error) {
                    querySelector_1.default('#test-label').innerHTML = version;
                }
                else {
                    querySelector_1.default('#test-label').innerHTML = 'Cannot retrieve jibo-version';
                }
            });
            const packageInfo = require(path.resolve('package.json'));
            querySelector_1.default('#test-label2').innerHTML = 'Diagnostic Version: ' + packageInfo.version;
            const div = querySelector_1.default('#misc-log');
            div.style.display = 'block';
            div.innerHTML += '<h4>Body Service Data</h4>';
            div.innerHTML += '<span id="temp" class="item"></span>';
            div.innerHTML += '<span id="fan-speed" class="item"></span>';
            div.innerHTML += '<span id="battery-level" class="item"></span>';
        });
    }
    exit() {
        super.exit();
        this.exiting = true;
        querySelector_1.default('#test-label').innerHTML = '';
        querySelector_1.default('#test-label2').innerHTML = '';
        querySelector_1.default('#test-label3').innerHTML = '';
        let div = querySelector_1.default('#misc-log');
        div.style.display = 'none';
        div.innerHTML = '';
    }
    update() {
        if (this.exiting) {
            return;
        }
        const batTemp = jibo.system.getBatteryTemperature();
        const mainTemp = jibo.system.getMainBoardTemperature();
        const cpuTemp = jibo.system.getCPUTemperature();
        {
            const tempDiv = querySelector_1.default('#temp');
            if (tempDiv) {
                tempDiv.innerHTML = 'Battery | Main Board | CPU temps: ' + batTemp + ' | ' + mainTemp + ' | ' + cpuTemp + ' (C)';
            }
        }
        function _updateFanDiv(value) {
            const fanDiv = querySelector_1.default('#fan-speed');
            if (fanDiv) {
                fanDiv.innerHTML = 'Fan speed: ' + value + '%';
            }
        }
        if (this.bodyConnected) {
            jibo.system.getFanSpeed((error, result) => {
                if (error) {
                    this.bodyConnected = false;
                }
                const value = (error ? '0' : Math.round(100.0 * result));
                _updateFanDiv(value);
            });
        }
        else {
            _updateFanDiv(0);
        }
        const charging = (jibo.system.batteryCharging ? 'charging' : 'not charging');
        const batteryLevel = jibo.system.getBatteryLevel();
        const sysVolt = jibo.system.getSystemVoltage();
        {
            const tempDiv = querySelector_1.default('#battery-level');
            if (tempDiv) {
                tempDiv.innerHTML = 'Battery (' + charging + '): ' + Math.round(batteryLevel) + '%' + ' (vsys ' + sysVolt + ')';
            }
        }
    }
}
exports.default = NoTest;

},{"../BaseTest":1,"../utils/querySelector":24,"jibo":undefined,"path":undefined}],18:[function(require,module,exports){
"use strict";
var __awaiter = (this && this.__awaiter) || function (thisArg, _arguments, P, generator) {
    return new (P || (P = Promise))(function (resolve, reject) {
        function fulfilled(value) { try { step(generator.next(value)); } catch (e) { reject(e); } }
        function rejected(value) { try { step(generator["throw"](value)); } catch (e) { reject(e); } }
        function step(result) { result.done ? resolve(result.value) : new P(function (resolve) { resolve(result.value); }).then(fulfilled, rejected); }
        step((generator = generator.apply(thisArg, _arguments || [])).next());
    });
};
Object.defineProperty(exports, "__esModule", { value: true });
const BaseTest_1 = require("../BaseTest");
const jibo = require("jibo");
const querySelector_1 = require("../utils/querySelector");
class OTATest extends BaseTest_1.default {
    constructor(testName) {
        super(testName);
        this._haveCredentials = false;
        this.downloading = false;
        this.installing = false;
        this.dirty = false;
    }
    enter() {
        const _super = name => super[name];
        return __awaiter(this, void 0, void 0, function* () {
            _super("enter").call(this);
            querySelector_1.default('#test-label').innerHTML = 'No updates available';
            querySelector_1.default('#test-label2').innerHTML = '';
            this.exiting = false;
            jibo.systemManager.getCredentials((error) => {
                if (error) {
                    this._log.info('No credentials set? ' + error);
                    querySelector_1.default('#test-label').innerHTML = 'Cannot OTA. Get credentials on your robot first!';
                }
                else {
                    this._haveCredentials = true;
                }
            });
        });
    }
    exit() {
        super.exit();
        querySelector_1.default('#test-label').innerHTML = '';
        querySelector_1.default('#test-label2').innerHTML = '';
        this.exiting = true;
    }
    click(event) {
        super.click(event);
        if (this.exiting ||
            this.downloading ||
            this.installing ||
            !this._haveCredentials) {
            return;
        }
        jibo.systemManager.checkForUpdates((error, updateList) => {
            if (error) {
                this._log.info('Error during checkForUpdates: ' + error);
            }
            else {
                let downloadData = { 'ids': [] };
                let installData = { 'ids': [] };
                for (let i = 0; i < updateList.length; i++) {
                    if (updateList[i].downloaded) {
                        installData.ids.push(updateList[i].id);
                    }
                    else {
                        downloadData.ids.push(updateList[i].id);
                        break;
                    }
                }
                if (downloadData.ids.length > 0) {
                    this.downloading = true;
                    querySelector_1.default('#test-label2').innerHTML = 'Downloading...';
                    jibo.systemManager.downloadUpdates(downloadData, (error, data) => {
                        this._downloadCB(error, data);
                    });
                }
                else if (installData.ids.length > 0) {
                    this.installing = true;
                    let label = querySelector_1.default('#test-label2');
                    label.innerHTML = 'Installing...see you on the other side (hopefully)!';
                    jibo.systemManager.installUpdates(installData, (error) => {
                        if (error) {
                            label.innerHTML = 'Error installing! (' + error + ')';
                        }
                    });
                }
            }
        });
    }
    _downloadCB(error, data) {
        if (!this.downloading) {
            return;
        }
        let progressMsg = '';
        if (error) {
            progressMsg = 'Download error: ' + error;
            this.downloading = false;
        }
        else if (data) {
            if (data.status === 'finished') {
                progressMsg = 'Download finished!';
                this.downloading = false;
                this.dirty = true;
            }
            else if (data.status === 'downloading') {
                progressMsg = 'Downloading (' + Math.round((data.received / data.length * 100)) + '%)';
                this.downloading = true;
            }
            else if (data.status === 'failed') {
                progressMsg = 'Download failed: ' + data.reason;
                this.downloading = false;
                this.dirty = true;
            }
        }
        if (progressMsg.length > 0) {
            this._log.info(progressMsg);
        }
        querySelector_1.default('#test-label2').innerHTML = progressMsg;
    }
    _updateProgress(updateList) {
        let downloaded = 0;
        let available = 0;
        if (updateList && updateList.length > 0) {
            available = updateList.length;
        }
        for (let i = 0; i < available; i++) {
            if (updateList[i].downloaded) {
                downloaded++;
            }
        }
        let header = 'OTAs (downloaded/available): ' + downloaded + '/' + available;
        querySelector_1.default('#test-label').innerHTML = header;
        if (available > 0) {
            if (downloaded < available) {
                querySelector_1.default('#test-label2').innerHTML = 'Click to download';
            }
            else if (downloaded === available) {
                querySelector_1.default('#test-label2').innerHTML = 'Click to install';
            }
        }
    }
    update() {
        if (this.dirty && !this.exiting) {
            this.dirty = false;
            jibo.systemManager.checkForUpdates((error, updateList) => {
                if (error) {
                    this._log.info('Error during checkForUpdates: ' + error);
                }
                else {
                    this._updateProgress(updateList);
                }
            });
        }
    }
}
exports.default = OTATest;

},{"../BaseTest":1,"../utils/querySelector":24,"jibo":undefined}],19:[function(require,module,exports){
"use strict";
var __awaiter = (this && this.__awaiter) || function (thisArg, _arguments, P, generator) {
    return new (P || (P = Promise))(function (resolve, reject) {
        function fulfilled(value) { try { step(generator.next(value)); } catch (e) { reject(e); } }
        function rejected(value) { try { step(generator["throw"](value)); } catch (e) { reject(e); } }
        function step(result) { result.done ? resolve(result.value) : new P(function (resolve) { resolve(result.value); }).then(fulfilled, rejected); }
        step((generator = generator.apply(thisArg, _arguments || [])).next());
    });
};
Object.defineProperty(exports, "__esModule", { value: true });
const BaseTest_1 = require("../BaseTest");
const jibo_1 = require("jibo");
const querySelector_1 = require("../utils/querySelector");
class PhotoTest extends BaseTest_1.default {
    constructor(testName) {
        super(testName);
        this.running = false;
        this.autoRun = true;
        this.myIntervalId = undefined;
        this.autoRunAttempts = 0;
        this.autoRunSuccesses = 0;
    }
    enter() {
        const _super = name => super[name];
        return __awaiter(this, void 0, void 0, function* () {
            _super("enter").call(this);
            this.running = true;
            querySelector_1.default('#face').style.display = 'none';
            if (this.autoRun) {
                querySelector_1.default('#test-label').innerHTML = 'Auto-run photo test';
            }
            else {
                querySelector_1.default('#test-label').innerHTML = 'Click to take photo';
            }
            this._setShowBackground(false, '');
            this.autoRunAttempts = 0;
            this.autoRunSuccesses = 0;
            if (this.autoRun) {
                this.myIntervalId = setInterval(() => {
                    if (this.autoRunAttempts < 20) {
                        this._takePhoto();
                        const failures = this.autoRunAttempts - this.autoRunSuccesses;
                        this.autoRunAttempts++;
                        querySelector_1.default('#test-label').innerHTML = ('Auto-run attempts: ' + this.autoRunAttempts + '/20');
                        if (failures >= 0) {
                            querySelector_1.default('#test-label2').innerHTML = ('(' + failures + ' failures)');
                        }
                    }
                    else {
                        this.stop();
                    }
                }, 3000);
            }
        });
    }
    stop() {
        if (this.autoRun && (this.myIntervalId !== undefined)) {
            clearInterval(this.myIntervalId);
            querySelector_1.default('#test-label').innerHTML = 'Click to take photo';
            let failures = this.autoRunAttempts - this.autoRunSuccesses;
            querySelector_1.default('#test-label2').innerHTML = ('(Last auto-run: ' + this.autoRunAttempts + ' attempts and ' + failures + ' failures)');
            this.myIntervalId = null;
            this.autoRunAttempts = 0;
            this.autoRunSuccesses = 0;
            this.autoRun = false;
            this._setShowBackground(false, '');
        }
    }
    exit() {
        super.exit();
        this.stop();
        querySelector_1.default('#test-label').innerHTML = '';
        querySelector_1.default('#test-label2').innerHTML = '';
        this._setShowBackground(false, '');
        querySelector_1.default('#face').style.display = 'block';
        this.running = false;
    }
    click(event) {
        super.click(event);
        this._takePhoto();
    }
    _takePhoto() {
        this._setShowBackground(false, '');
        if (!this.autoRun) {
            querySelector_1.default('#test-label2').innerHTML = '';
        }
        jibo_1.media.takePhoto((error, data) => {
            if (!this.running) {
                return;
            }
            const bkg = querySelector_1.default('#background');
            if (error) {
                querySelector_1.default('#test-label').innerHTML = error;
                this._setShowBackground(true, 'assets/off-air.gif');
            }
            else {
                this._log.info('photo taken');
                this._setShowBackground(true, data.url);
                if (this.autoRun) {
                    this.autoRunSuccesses++;
                    const failures = this.autoRunAttempts - this.autoRunSuccesses;
                    if (failures >= 0) {
                        querySelector_1.default('#test-label2').innerHTML = ('(' + failures + ' failures)');
                    }
                }
            }
        });
    }
    _setShowBackground(show, source) {
        const bkg = querySelector_1.default('#background');
        bkg.style.display = (show ? 'block' : 'none');
        bkg.src = source;
    }
}
exports.default = PhotoTest;

},{"../BaseTest":1,"../utils/querySelector":24,"jibo":undefined}],20:[function(require,module,exports){
"use strict";
var __awaiter = (this && this.__awaiter) || function (thisArg, _arguments, P, generator) {
    return new (P || (P = Promise))(function (resolve, reject) {
        function fulfilled(value) { try { step(generator.next(value)); } catch (e) { reject(e); } }
        function rejected(value) { try { step(generator["throw"](value)); } catch (e) { reject(e); } }
        function step(result) { result.done ? resolve(result.value) : new P(function (resolve) { resolve(result.value); }).then(fulfilled, rejected); }
        step((generator = generator.apply(thisArg, _arguments || [])).next());
    });
};
Object.defineProperty(exports, "__esModule", { value: true });
const BaseTest_1 = require("../BaseTest");
const jibo_1 = require("jibo");
const querySelector_1 = require("../utils/querySelector");
class TTSTest extends BaseTest_1.default {
    constructor(testName) {
        super(testName);
    }
    enter() {
        const _super = name => super[name];
        return __awaiter(this, void 0, void 0, function* () {
            _super("enter").call(this);
            jibo_1.tts.speak('Hello, my name is Jibo', function (error) {
                if (error) {
                    querySelector_1.default('#test-label').innerHTML = error;
                }
            });
            jibo_1.tts.word.on(function (word) {
                querySelector_1.default('#test-label').innerHTML = word.token;
            });
        });
    }
    exit() {
        super.exit();
        querySelector_1.default('#test-label').innerHTML = '';
        jibo_1.tts.word.removeAllListeners();
    }
}
exports.default = TTSTest;

},{"../BaseTest":1,"../utils/querySelector":24,"jibo":undefined}],21:[function(require,module,exports){
"use strict";
var __awaiter = (this && this.__awaiter) || function (thisArg, _arguments, P, generator) {
    return new (P || (P = Promise))(function (resolve, reject) {
        function fulfilled(value) { try { step(generator.next(value)); } catch (e) { reject(e); } }
        function rejected(value) { try { step(generator["throw"](value)); } catch (e) { reject(e); } }
        function step(result) { result.done ? resolve(result.value) : new P(function (resolve) { resolve(result.value); }).then(fulfilled, rejected); }
        step((generator = generator.apply(thisArg, _arguments || [])).next());
    });
};
Object.defineProperty(exports, "__esModule", { value: true });
const BaseTest_1 = require("../BaseTest");
const jibo = require("jibo");
const querySelector_1 = require("../utils/querySelector");
const tracking = require('../behaviors/tracking');
class TrackingTest extends BaseTest_1.default {
    constructor(testName) {
        super(testName);
        this.root = jibo.bt.create(tracking);
    }
    enter() {
        const _super = name => super[name];
        return __awaiter(this, void 0, void 0, function* () {
            _super("enter").call(this);
            querySelector_1.default('#test-label').innerHTML = 'Look at me!';
            this.root.start();
        });
    }
    exit() {
        super.exit();
        querySelector_1.default('#test-label').innerHTML = '';
        this.root.stop();
    }
    update() {
        this.root.update();
    }
}
exports.default = TrackingTest;

},{"../BaseTest":1,"../behaviors/tracking":7,"../utils/querySelector":24,"jibo":undefined}],22:[function(require,module,exports){
"use strict";
var __awaiter = (this && this.__awaiter) || function (thisArg, _arguments, P, generator) {
    return new (P || (P = Promise))(function (resolve, reject) {
        function fulfilled(value) { try { step(generator.next(value)); } catch (e) { reject(e); } }
        function rejected(value) { try { step(generator["throw"](value)); } catch (e) { reject(e); } }
        function step(result) { result.done ? resolve(result.value) : new P(function (resolve) { resolve(result.value); }).then(fulfilled, rejected); }
        step((generator = generator.apply(thisArg, _arguments || [])).next());
    });
};
Object.defineProperty(exports, "__esModule", { value: true });
const BaseTest_1 = require("../BaseTest");
const querySelector_1 = require("../utils/querySelector");
class WebGLTest extends BaseTest_1.default {
    constructor(testName) {
        super(testName);
    }
    enter() {
        const _super = name => super[name];
        return __awaiter(this, void 0, void 0, function* () {
            _super("enter").call(this);
            const webDiv = querySelector_1.default('#webgl');
            webDiv.src = 'http://webglreport.com/?v=1';
            webDiv.style.display = 'block';
        });
    }
    exit() {
        super.exit();
        const webDiv = querySelector_1.default('#webgl');
        webDiv.src = '';
        webDiv.style.display = 'none';
    }
}
exports.default = WebGLTest;

},{"../BaseTest":1,"../utils/querySelector":24}],23:[function(require,module,exports){
"use strict";
Object.defineProperty(exports, "__esModule", { value: true });
const ASRTest_1 = require("./ASRTest");
const AudioTest_1 = require("./AudioTest");
const AudioTrackingTest_1 = require("./AudioTrackingTest");
const BodyTest_1 = require("./BodyTest");
const LEDTest_1 = require("./LEDTest");
const LifeCycleTest_1 = require("./LifeCycleTest");
const MathTest_1 = require("./MathTest");
const NoTest_1 = require("./NoTest");
const OTATest_1 = require("./OTATest");
const PhotoTest_1 = require("./PhotoTest");
const TrackingTest_1 = require("./TrackingTest");
const TTSTest_1 = require("./TTSTest");
const WebGLTest_1 = require("./WebGLTest");
exports.default = {
    ASRTest: ASRTest_1.default,
    AudioTest: AudioTest_1.default,
    AudioTrackingTest: AudioTrackingTest_1.default,
    BodyTest: BodyTest_1.default,
    LEDTest: LEDTest_1.default,
    LifeCycleTest: LifeCycleTest_1.default,
    MathTest: MathTest_1.default,
    NoTest: NoTest_1.default,
    OTATest: OTATest_1.default,
    PhotoTest: PhotoTest_1.default,
    TrackingTest: TrackingTest_1.default,
    TTSTest: TTSTest_1.default,
    WebGLTest: WebGLTest_1.default
};

},{"./ASRTest":10,"./AudioTest":11,"./AudioTrackingTest":12,"./BodyTest":13,"./LEDTest":14,"./LifeCycleTest":15,"./MathTest":16,"./NoTest":17,"./OTATest":18,"./PhotoTest":19,"./TTSTest":20,"./TrackingTest":21,"./WebGLTest":22}],24:[function(require,module,exports){
"use strict";
Object.defineProperty(exports, "__esModule", { value: true });
exports.default = document.querySelector.bind(document);

},{}],25:[function(require,module,exports){
"use strict";
Object.defineProperty(exports, "__esModule", { value: true });
const Stats = function () {
    const now = (self.performance && self.performance.now) ? self.performance.now.bind(performance) : Date.now;
    let startTime = now(), prevTime = startTime;
    let frames = 0, mode = 0;
    function createElement(tag, id, css) {
        const element = document.createElement(tag);
        element.id = id;
        element.style.cssText = css;
        return element;
    }
    function createPanel(id, fg, bg) {
        const div = createElement('div', id, 'padding:0 0 3px 3px;text-align:left;background:' + bg);
        const text = createElement('div', id + 'Text', 'font-family:Helvetica,Arial,sans-serif;font-size:9px;font-weight:bold;line-height:15px;color:' + fg);
        text.innerHTML = id.toUpperCase();
        div.appendChild(text);
        const graph = createElement('div', id + 'Graph', 'width:74px;height:30px;background:' + fg);
        div.appendChild(graph);
        for (let i = 0; i < 74; i++) {
            graph.appendChild(createElement('span', '', 'width:1px;height:30px;float:left;opacity:0.9;background:' + bg));
        }
        return div;
    }
    function setMode(value) {
        const children = container.children;
        for (let i = 0; i < children.length; i++) {
            children[i].style.display = i === value ? 'block' : 'none';
        }
        mode = value;
    }
    function updateGraph(dom, value) {
        const child = dom.appendChild(dom.firstChild);
        child.style.height = Math.min(30, 30 - value * 30) + 'px';
    }
    const container = createElement('div', 'stats', 'width:80px;opacity:0.9;cursor:pointer');
    container.addEventListener('mousedown', function (event) {
        event.preventDefault();
        setMode(++mode % container.children.length);
    }, false);
    let fps = 0, fpsMin = Infinity, fpsMax = 0;
    const fpsDiv = createPanel('fps', '#0ff', '#002');
    const fpsText = fpsDiv.children[0];
    const fpsGraph = fpsDiv.children[1];
    container.appendChild(fpsDiv);
    let ms = 0, msMin = Infinity, msMax = 0;
    const msDiv = createPanel('ms', '#0f0', '#020');
    const msText = msDiv.children[0];
    const msGraph = msDiv.children[1];
    container.appendChild(msDiv);
    let mem, memMin, memMax, memText, memGraph;
    if (self.performance && self.performance.memory) {
        memMin = Infinity;
        memMax = 0;
        mem = 0;
        const memDiv = createPanel('mb', '#f08', '#201');
        memText = memDiv.children[0];
        memGraph = memDiv.children[1];
        container.appendChild(memDiv);
    }
    setMode(mode);
    return {
        REVISION: 14,
        domElement: container,
        setMode,
        begin: () => {
            startTime = now();
        },
        end: () => {
            const time = now();
            ms = time - startTime;
            msMin = Math.min(msMin, ms);
            msMax = Math.max(msMax, ms);
            msText.textContent = (ms | 0) + ' MS (' + (msMin | 0) + '-' + (msMax | 0) + ')';
            updateGraph(msGraph, ms / 200);
            frames++;
            if (time > prevTime + 1000) {
                fps = Math.round((frames * 1000) / (time - prevTime));
                fpsMin = Math.min(fpsMin, fps);
                fpsMax = Math.max(fpsMax, fps);
                fpsText.textContent = fps + ' FPS (' + fpsMin + '-' + fpsMax + ')';
                updateGraph(fpsGraph, fps / 100);
                prevTime = time;
                frames = 0;
                if (mem !== undefined) {
                    let heapSize = performance.memory.usedJSHeapSize;
                    let heapSizeLimit = performance.memory.jsHeapSizeLimit;
                    mem = Math.round(heapSize * 0.000000954);
                    memMin = Math.min(memMin, mem);
                    memMax = Math.max(memMax, mem);
                    memText.textContent = mem + ' MB (' + memMin + '-' + memMax + ')';
                    updateGraph(memGraph, heapSize / heapSizeLimit);
                }
            }
            return time;
        },
        update: function () {
            startTime = this.end();
        }
    };
};
const stats = new Stats();
stats.setMode(0);
if (document.readyState === 'complete') {
    init();
}
else {
    document.addEventListener('DOMContentLoaded', function (event) {
        init();
    });
}
function init() {
    stats.id = 'jibo-stats';
    stats.domElement.style.position = 'absolute';
    stats.domElement.style.left = '0px';
    stats.domElement.style.top = '0px';
    stats.domElement.style.zIndex = 1000;
    stats.domElement.style.transform = 'scale(2.5,2.5)';
    stats.domElement.style.transformOrigin = '0% 0%';
    document.body.appendChild(stats.domElement);
    const update = function () {
        stats.begin();
        stats.end();
        requestAnimationFrame(update);
    };
    requestAnimationFrame(update);
}
exports.default = stats;

},{}]},{},[8])
//# sourceMappingURL=index.js.map