












Examples
The Prism source, highlighted with Prism (don’t you just love how meta this is?):


/* **********************************************
     Begin prism-core.js
********************************************** */

/// <reference lib="WebWorker"/>

var _self = (typeof window !== 'undefined')
	? window   // if in browser
	: (
		(typeof WorkerGlobalScope !== 'undefined' && self instanceof WorkerGlobalScope)
			? self // if in worker
			: {}   // if in node js
	);

/**
 * Prism: Lightweight, robust, elegant syntax highlighting
 *
 * @license MIT <https://opensource.org/licenses/MIT>
 * @author Lea Verou <https://lea.verou.me>
 * @namespace
 * @public
 */
var Prism = (function (_self) {

	// Private helper vars
	var lang = /(?:^|\s)lang(?:uage)?-([\w-]+)(?=\s|$)/i;
	var uniqueId = 0;

	// The grammar object for plaintext
	var plainTextGrammar = {};


	var _ = {
		/**
		 * By default, Prism will attempt to highlight all code elements (by calling {@link Prism.highlightAll}) on the
		 * current page after the page finished loading. This might be a problem if e.g. you wanted to asynchronously load
		 * additional languages or plugins yourself.
		 *
		 * By setting this value to `true`, Prism will not automatically highlight all code elements on the page.
		 *
		 * You obviously have to change this value before the automatic highlighting started. To do this, you can add an
		 * empty Prism object into the global scope before loading the Prism script like this:
		 *
		 * ```js
		 * window.Prism = window.Prism || {};
		 * Prism.manual = true;
		 * // add a new <script> to load Prism's script
		 * ```
		 *
		 * @default false
		 * @type {boolean}
		 * @memberof Prism
		 * @public
		 */
		manual: _self.Prism && _self.Prism.manual,
		/**
		 * By default, if Prism is in a web worker, it assumes that it is in a worker it created itself, so it uses
		 * `addEventListener` to communicate with its parent instance. However, if you're using Prism manually in your
		 * own worker, you don't want it to do this.
		 *
		 * By setting this value to `true`, Prism will not add its own listeners to the worker.
		 *
		 * You obviously have to change this value before Prism executes. To do this, you can add an
		 * empty Prism object into the global scope before loading the Prism script like this:
		 *
		 * ```js
		 * window.Prism = window.Prism || {};
		 * Prism.disableWorkerMessageHandler = true;
		 * // Load Prism's script
		 * ```
		 *
		 * @default false
		 * @type {boolean}
		 * @memberof Prism
		 * @public
		 */
		disableWorkerMessageHandler: _self.Prism && _self.Prism.disableWorkerMessageHandler,

		/**
		 * A namespace for utility methods.
		 *
		 * All function in this namespace that are not explicitly marked as _public_ are for __internal use only__ and may
		 * change or disappear at any time.
		 *
		 * @namespace
		 * @memberof Prism
		 */
		util: {
			encode: function encode(tokens) {
				if (tokens instanceof Token) {
					return new Token(tokens.type, encode(tokens.content), tokens.alias);
				} else if (Array.isArray(tokens)) {
					return tokens.map(encode);
				} else {
					return tokens.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/\u00a0/g, ' ');
				}
			},

			/**
			 * Returns the name of the type of the given value.
			 *
			 * @param {any} o
			 * @returns {string}
			 * @example
			 * type(null)      === 'Null'
			 * type(undefined) === 'Undefined'
			 * type(123)       === 'Number'
			 * type('foo')     === 'String'
			 * type(true)      === 'Boolean'
			 * type([1, 2])    === 'Array'
			 * type({})        === 'Object'
			 * type(String)    === 'Function'
			 * type(/abc+/)    === 'RegExp'
			 */
			type: function (o) {
				return Object.prototype.toString.call(o).slice(8, -1);
			},

			/**
			 * Returns a unique number for the given object. Later calls will still return the same number.
			 *
			 * @param {Object} obj
			 * @returns {number}
			 */
			objId: function (obj) {
				if (!obj['__id']) {
					Object.defineProperty(obj, '__id', { value: ++uniqueId });
				}
				return obj['__id'];
			},

			/**
			 * Creates a deep clone of the given object.
			 *
			 * The main intended use of this function is to clone language definitions.
			 *
			 * @param {T} o
			 * @param {Record<number, any>} [visited]
			 * @returns {T}
			 * @template T
			 */
			clone: function deepClone(o, visited) {
				visited = visited || {};

				var clone; var id;
				switch (_.util.type(o)) {
					case 'Object':
						id = _.util.objId(o);
						if (visited[id]) {
							return visited[id];
						}
						clone = /** @type {Record<string, any>} */ ({});
						visited[id] = clone;

						for (var key in o) {
							if (o.hasOwnProperty(key)) {
								clone[key] = deepClone(o[key], visited);
							}
						}

						return /** @type {any} */ (clone);

					case 'Array':
						id = _.util.objId(o);
						if (visited[id]) {
							return visited[id];
						}
						clone = [];
						visited[id] = clone;

						(/** @type {Array} */(/** @type {any} */(o))).forEach(function (v, i) {
							clone[i] = deepClone(v, visited);
						});

						return /** @type {any} */ (clone);

					default:
						return o;
				}
			},

			/**
			 * Returns the Prism language of the given element set by a `language-xxxx` or `lang-xxxx` class.
			 *
			 * If no language is set for the element or the element is `null` or `undefined`, `none` will be returned.
			 *
			 * @param {Element} element
			 * @returns {string}
			 */
			getLanguage: function (element) {
				while (element) {
					var m = lang.exec(element.className);
					if (m) {
						return m[1].toLowerCase();
					}
					element = element.parentElement;
				}
				return 'none';
			},

			/**
			 * Sets the Prism `language-xxxx` class of the given element.
			 *
			 * @param {Element} element
			 * @param {string} language
			 * @returns {void}
			 */
			setLanguage: function (element, language) {
				// remove all `language-xxxx` classes
				// (this might leave behind a leading space)
				element.className = element.className.replace(RegExp(lang, 'gi'), '');

				// add the new `language-xxxx` class
				// (using `classList` will automatically clean up spaces for us)
				element.classList.add('language-' + language);
			},

			/**
			 * Returns the script element that is currently executing.
			 *
			 * This does __not__ work for line script element.
			 *
			 * @returns {HTMLScriptElement | null}
			 */
			currentScript: function () {
				if (typeof document === 'undefined') {
					return null;
				}
				if (document.currentScript && document.currentScript.tagName === 'SCRIPT' && 1 < 2 /* hack to trip TS' flow analysis */) {
					return /** @type {any} */ (document.currentScript);
				}

				// IE11 workaround
				// we'll get the src of the current script by parsing IE11's error stack trace
				// this will not work for inline scripts

				try {
					throw new Error();
				} catch (err) {
					// Get file src url from stack. Specifically works with the format of stack traces in IE.
					// A stack will look like this:
					//
					// Error
					//    at _.util.currentScript (http://localhost/components/prism-core.js:119:5)
					//    at Global code (http://localhost/components/prism-core.js:606:1)

					var src = (/at [^(\r\n]*\((.*):[^:]+:[^:]+\)$/i.exec(err.stack) || [])[1];
					if (src) {
						var scripts = document.getElementsByTagName('script');
						for (var i in scripts) {
							if (scripts[i].src == src) {
								return scripts[i];
							}
						}
					}
					return null;
				}
			},

			/**
			 * Returns whether a given class is active for `element`.
			 *
			 * The class can be activated if `element` or one of its ancestors has the given class and it can be deactivated
			 * if `element` or one of its ancestors has the negated version of the given class. The _negated version_ of the
			 * given class is just the given class with a `no-` prefix.
			 *
			 * Whether the class is active is determined by the closest ancestor of `element` (where `element` itself is
			 * closest ancestor) that has the given class or the negated version of it. If neither `element` nor any of its
			 * ancestors have the given class or the negated version of it, then the default activation will be returned.
			 *
			 * In the paradoxical situation where the closest ancestor contains __both__ the given class and the negated
			 * version of it, the class is considered active.
			 *
			 * @param {Element} element
			 * @param {string} className
			 * @param {boolean} [defaultActivation=false]
			 * @returns {boolean}
			 */
			isActive: function (element, className, defaultActivation) {
				var no = 'no-' + className;

				while (element) {
					var classList = element.classList;
					if (classList.contains(className)) {
						return true;
					}
					if (classList.contains(no)) {
						return false;
					}
					element = element.parentElement;
				}
				return !!defaultActivation;
			}
		},

		/**
		 * This namespace contains all currently loaded languages and the some helper functions to create and modify languages.
		 *
		 * @namespace
		 * @memberof Prism
		 * @public
		 */
		languages: {
			/**
			 * The grammar for plain, unformatted text.
			 */
			plain: plainTextGrammar,
			plaintext: plainTextGrammar,
			text: plainTextGrammar,
			txt: plainTextGrammar,

			/**
			 * Creates a deep copy of the language with the given id and appends the given tokens.
			 *
			 * If a token in `redef` also appears in the copied language, then the existing token in the copied language
			 * will be overwritten at its original position.
			 *
			 * ## Best practices
			 *
			 * Since the position of overwriting tokens (token in `redef` that overwrite tokens in the copied language)
			 * doesn't matter, they can technically be in any order. However, this can be confusing to others that trying to
			 * understand the language definition because, normally, the order of tokens matters in Prism grammars.
			 *
			 * Therefore, it is encouraged to order overwriting tokens according to the positions of the overwritten tokens.
			 * Furthermore, all non-overwriting tokens should be placed after the overwriting ones.
			 *
			 * @param {string} id The id of the language to extend. This has to be a key in `Prism.languages`.
			 * @param {Grammar} redef The new tokens to append.
			 * @returns {Grammar} The new language created.
			 * @public
			 * @example
			 * Prism.languages['css-with-colors'] = Prism.languages.extend('css', {
			 *     // Prism.languages.css already has a 'comment' token, so this token will overwrite CSS' 'comment' token
			 *     // at its original position
			 *     'comment': { ... },
			 *     // CSS doesn't have a 'color' token, so this token will be appended
			 *     'color': /\b(?:red|green|blue)\b/
			 * });
			 */
			extend: function (id, redef) {
				var lang = _.util.clone(_.languages[id]);

				for (var key in redef) {
					lang[key] = redef[key];
				}

				return lang;
			},

			/**
			 * Inserts tokens _before_ another token in a language definition or any other grammar.
			 *
			 * ## Usage
			 *
			 * This helper method makes it easy to modify existing languages. For example, the CSS language definition
			 * not only defines CSS highlighting for CSS documents, but also needs to define highlighting for CSS embedded
			 * in HTML through `<style>` elements. To do this, it needs to modify `Prism.languages.markup` and add the
			 * appropriate tokens. However, `Prism.languages.markup` is a regular JavaScript object literal, so if you do
			 * this:
			 *
			 * ```js
			 * Prism.languages.markup.style = {
			 *     // token
			 * };
			 * ```
			 *
			 * then the `style` token will be added (and processed) at the end. `insertBefore` allows you to insert tokens
			 * before existing tokens. For the CSS example above, you would use it like this:
			 *
			 * ```js
			 * Prism.languages.insertBefore('markup', 'cdata', {
			 *     'style': {
			 *         // token
			 *     }
			 * });
			 * ```
			 *
			 * ## Special cases
			 *
			 * If the grammars of `inside` and `insert` have tokens with the same name, the tokens in `inside`'s grammar
			 * will be ignored.
			 *
			 * This behavior can be used to insert tokens after `before`:
			 *
			 * ```js
			 * Prism.languages.insertBefore('markup', 'comment', {
			 *     'comment': Prism.languages.markup.comment,
			 *     // tokens after 'comment'
			 * });
			 * ```
			 *
			 * ## Limitations
			 *
			 * The main problem `insertBefore` has to solve is iteration order. Since ES2015, the iteration order for object
			 * properties is guaranteed to be the insertion order (except for integer keys) but some browsers behave
			 * differently when keys are deleted and re-inserted. So `insertBefore` can't be implemented by temporarily
			 * deleting properties which is necessary to insert at arbitrary positions.
			 *
			 * To solve this problem, `insertBefore` doesn't actually insert the given tokens into the target object.
			 * Instead, it will create a new object and replace all references to the target object with the new one. This
			 * can be done without temporarily deleting properties, so the iteration order is well-defined.
			 *
			 * However, only references that can be reached from `Prism.languages` or `insert` will be replaced. I.e. if
			 * you hold the target object in a variable, then the value of the variable will not change.
			 *
			 * ```js
			 * var oldMarkup = Prism.languages.markup;
			 * var newMarkup = Prism.languages.insertBefore('markup', 'comment', { ... });
			 *
			 * assert(oldMarkup !== Prism.languages.markup);
			 * assert(newMarkup === Prism.languages.markup);
			 * ```
			 *
			 * @param {string} inside The property of `root` (e.g. a language id in `Prism.languages`) that contains the
			 * object to be modified.
			 * @param {string} before The key to insert before.
			 * @param {Grammar} insert An object containing the key-value pairs to be inserted.
			 * @param {Object<string, any>} [root] The object containing `inside`, i.e. the object that contains the
			 * object to be modified.
			 *
			 * Defaults to `Prism.languages`.
			 * @returns {Grammar} The new grammar object.
			 * @public
			 */
			insertBefore: function (inside, before, insert, root) {
				root = root || /** @type {any} */ (_.languages);
				var grammar = root[inside];
				/** @type {Grammar} */
				var ret = {};

				for (var token in grammar) {
					if (grammar.hasOwnProperty(token)) {

						if (token == before) {
							for (var newToken in insert) {
								if (insert.hasOwnProperty(newToken)) {
									ret[newToken] = insert[newToken];
								}
							}
						}

						// Do not insert token which also occur in insert. See #1525
						if (!insert.hasOwnProperty(token)) {
							ret[token] = grammar[token];
						}
					}
				}

				var old = root[inside];
				root[inside] = ret;

				// Update references in other language definitions
				_.languages.DFS(_.languages, function (key, value) {
					if (value === old && key != inside) {
						this[key] = ret;
					}
				});

				return ret;
			},

			// Traverse a language definition with Depth First Search
			DFS: function DFS(o, callback, type, visited) {
				visited = visited || {};

				var objId = _.util.objId;

				for (var i in o) {
					if (o.hasOwnProperty(i)) {
						callback.call(o, i, o[i], type || i);

						var property = o[i];
						var propertyType = _.util.type(property);

						if (propertyType === 'Object' && !visited[objId(property)]) {
							visited[objId(property)] = true;
							DFS(property, callback, null, visited);
						} else if (propertyType === 'Array' && !visited[objId(property)]) {
							visited[objId(property)] = true;
							DFS(property, callback, i, visited);
						}
					}
				}
			}
		},

		plugins: {},

		/**
		 * This is the most high-level function in Prism’s API.
		 * It fetches all the elements that have a `.language-xxxx` class and then calls {@link Prism.highlightElement} on
		 * each one of them.
		 *
		 * This is equivalent to `Prism.highlightAllUnder(document, async, callback)`.
		 *
		 * @param {boolean} [async=false] Same as in {@link Prism.highlightAllUnder}.
		 * @param {HighlightCallback} [callback] Same as in {@link Prism.highlightAllUnder}.
		 * @memberof Prism
		 * @public
		 */
		highlightAll: function (async, callback) {
			_.highlightAllUnder(document, async, callback);
		},

		/**
		 * Fetches all the descendants of `container` that have a `.language-xxxx` class and then calls
		 * {@link Prism.highlightElement} on each one of them.
		 *
		 * The following hooks will be run:
		 * 1. `before-highlightall`
		 * 2. `before-all-elements-highlight`
		 * 3. All hooks of {@link Prism.highlightElement} for each element.
		 *
		 * @param {ParentNode} container The root element, whose descendants that have a `.language-xxxx` class will be highlighted.
		 * @param {boolean} [async=false] Whether each element is to be highlighted asynchronously using Web Workers.
		 * @param {HighlightCallback} [callback] An optional callback to be invoked on each element after its highlighting is done.
		 * @memberof Prism
		 * @public
		 */
		highlightAllUnder: function (container, async, callback) {
			var env = {
				callback: callback,
				container: container,
				selector: 'code[class*="language-"], [class*="language-"] code, code[class*="lang-"], [class*="lang-"] code'
			};

			_.hooks.run('before-highlightall', env);

			env.elements = Array.prototype.slice.apply(env.container.querySelectorAll(env.selector));

			_.hooks.run('before-all-elements-highlight', env);

			for (var i = 0, element; (element = env.elements[i++]);) {
				_.highlightElement(element, async === true, env.callback);
			}
		},

		/**
		 * Highlights the code inside a single element.
		 *
		 * The following hooks will be run:
		 * 1. `before-sanity-check`
		 * 2. `before-highlight`
		 * 3. All hooks of {@link Prism.highlight}. These hooks will be run by an asynchronous worker if `async` is `true`.
		 * 4. `before-insert`
		 * 5. `after-highlight`
		 * 6. `complete`
		 *
		 * Some the above hooks will be skipped if the element doesn't contain any text or there is no grammar loaded for
		 * the element's language.
		 *
		 * @param {Element} element The element containing the code.
		 * It must have a class of `language-xxxx` to be processed, where `xxxx` is a valid language identifier.
		 * @param {boolean} [async=false] Whether the element is to be highlighted asynchronously using Web Workers
		 * to improve performance and avoid blocking the UI when highlighting very large chunks of code. This option is
		 * [disabled by default](https://prismjs.com/faq.html#why-is-asynchronous-highlighting-disabled-by-default).
		 *
		 * Note: All language definitions required to highlight the code must be included in the main `prism.js` file for
		 * asynchronous highlighting to work. You can build your own bundle on the
		 * [Download page](https://prismjs.com/download.html).
		 * @param {HighlightCallback} [callback] An optional callback to be invoked after the highlighting is done.
		 * Mostly useful when `async` is `true`, since in that case, the highlighting is done asynchronously.
		 * @memberof Prism
		 * @public
		 */
		highlightElement: function (element, async, callback) {
			// Find language
			var language = _.util.getLanguage(element);
			var grammar = _.languages[language];

			// Set language on the element, if not present
			_.util.setLanguage(element, language);

			// Set language on the parent, for styling
			var parent = element.parentElement;
			if (parent && parent.nodeName.toLowerCase() === 'pre') {
				_.util.setLanguage(parent, language);
			}

			var code = element.textContent;

			var env = {
				element: element,
				language: language,
				grammar: grammar,
				code: code
			};

			function insertHighlightedCode(highlightedCode) {
				env.highlightedCode = highlightedCode;

				_.hooks.run('before-insert', env);

				env.element.innerHTML = env.highlightedCode;

				_.hooks.run('after-highlight', env);
				_.hooks.run('complete', env);
				callback && callback.call(env.element);
			}

			_.hooks.run('before-sanity-check', env);

			// plugins may change/add the parent/element
			parent = env.element.parentElement;
			if (parent && parent.nodeName.toLowerCase() === 'pre' && !parent.hasAttribute('tabindex')) {
				parent.setAttribute('tabindex', '0');
			}

			if (!env.code) {
				_.hooks.run('complete', env);
				callback && callback.call(env.element);
				return;
			}

			_.hooks.run('before-highlight', env);

			if (!env.grammar) {
				insertHighlightedCode(_.util.encode(env.code));
				return;
			}

			if (async && _self.Worker) {
				var worker = new Worker(_.filename);

				worker.onmessage = function (evt) {
					insertHighlightedCode(evt.data);
				};

				worker.postMessage(JSON.stringify({
					language: env.language,
					code: env.code,
					immediateClose: true
				}));
			} else {
				insertHighlightedCode(_.highlight(env.code, env.grammar, env.language));
			}
		},

		/**
		 * Low-level function, only use if you know what you’re doing. It accepts a string of text as input
		 * and the language definitions to use, and returns a string with the HTML produced.
		 *
		 * The following hooks will be run:
		 * 1. `before-tokenize`
		 * 2. `after-tokenize`
		 * 3. `wrap`: On each {@link Token}.
		 *
		 * @param {string} text A string with the code to be highlighted.
		 * @param {Grammar} grammar An object containing the tokens to use.
		 *
		 * Usually a language definition like `Prism.languages.markup`.
		 * @param {string} language The name of the language definition passed to `grammar`.
		 * @returns {string} The highlighted HTML.
		 * @memberof Prism
		 * @public
		 * @example
		 * Prism.highlight('var foo = true;', Prism.languages.javascript, 'javascript');
		 */
		highlight: function (text, grammar, language) {
			var env = {
				code: text,
				grammar: grammar,
				language: language
			};
			_.hooks.run('before-tokenize', env);
			if (!env.grammar) {
				throw new Error('The language "' + env.language + '" has no grammar.');
			}
			env.tokens = _.tokenize(env.code, env.grammar);
			_.hooks.run('after-tokenize', env);
			return Token.stringify(_.util.encode(env.tokens), env.language);
		},

		/**
		 * This is the heart of Prism, and the most low-level function you can use. It accepts a string of text as input
		 * and the language definitions to use, and returns an array with the tokenized code.
		 *
		 * When the language definition includes nested tokens, the function is called recursively on each of these tokens.
		 *
		 * This method could be useful in other contexts as well, as a very crude parser.
		 *
		 * @param {string} text A string with the code to be highlighted.
		 * @param {Grammar} grammar An object containing the tokens to use.
		 *
		 * Usually a language definition like `Prism.languages.markup`.
		 * @returns {TokenStream} An array of strings and tokens, a token stream.
		 * @memberof Prism
		 * @public
		 * @example
		 * let code = `var foo = 0;`;
		 * let tokens = Prism.tokenize(code, Prism.languages.javascript);
		 * tokens.forEach(token => {
		 *     if (token instanceof Prism.Token && token.type === 'number') {
		 *         console.log(`Found numeric literal: ${token.content}`);
		 *     }
		 * });
		 */
		tokenize: function (text, grammar) {
			var rest = grammar.rest;
			if (rest) {
				for (var token in rest) {
					grammar[token] = rest[token];
				}

				delete grammar.rest;
			}

			var tokenList = new LinkedList();
			addAfter(tokenList, tokenList.head, text);

			matchGrammar(text, tokenList, grammar, tokenList.head, 0);

			return toArray(tokenList);
		},

		/**
		 * @namespace
		 * @memberof Prism
		 * @public
		 */
		hooks: {
			all: {},

			/**
			 * Adds the given callback to the list of callbacks for the given hook.
			 *
			 * The callback will be invoked when the hook it is registered for is run.
			 * Hooks are usually directly run by a highlight function but you can also run hooks yourself.
			 *
			 * One callback function can be registered to multiple hooks and the same hook multiple times.
			 *
			 * @param {string} name The name of the hook.
			 * @param {HookCallback} callback The callback function which is given environment variables.
			 * @public
			 */
			add: function (name, callback) {
				var hooks = _.hooks.all;

				hooks[name] = hooks[name] || [];

				hooks[name].push(callback);
			},

			/**
			 * Runs a hook invoking all registered callbacks with the given environment variables.
			 *
			 * Callbacks will be invoked synchronously and in the order in which they were registered.
			 *
			 * @param {string} name The name of the hook.
			 * @param {Object<string, any>} env The environment variables of the hook passed to all callbacks registered.
			 * @public
			 */
			run: function (name, env) {
				var callbacks = _.hooks.all[name];

				if (!callbacks || !callbacks.length) {
					return;
				}

				for (var i = 0, callback; (callback = callbacks[i++]);) {
					callback(env);
				}
			}
		},

		Token: Token
	};
	_self.Prism = _;


	// Typescript note:
	// The following can be used to import the Token type in JSDoc:
	//
	//   @typedef {InstanceType<import("./prism-core")["Token"]>} Token

	/**
	 * Creates a new token.
	 *
	 * @param {string} type See {@link Token#type type}
	 * @param {string | TokenStream} content See {@link Token#content content}
	 * @param {string|string[]} [alias] The alias(es) of the token.
	 * @param {string} [matchedStr=""] A copy of the full string this token was created from.
	 * @class
	 * @global
	 * @public
	 */
	function Token(type, content, alias, matchedStr) {
		/**
		 * The type of the token.
		 *
		 * This is usually the key of a pattern in a {@link Grammar}.
		 *
		 * @type {string}
		 * @see GrammarToken
		 * @public
		 */
		this.type = type;
		/**
		 * The strings or tokens contained by this token.
		 *
		 * This will be a token stream if the pattern matched also defined an `inside` grammar.
		 *
		 * @type {string | TokenStream}
		 * @public
		 */
		this.content = content;
		/**
		 * The alias(es) of the token.
		 *
		 * @type {string|string[]}
		 * @see GrammarToken
		 * @public
		 */
		this.alias = alias;
		// Copy of the full string this token was created from
		this.length = (matchedStr || '').length | 0;
	}

	/**
	 * A token stream is an array of strings and {@link Token Token} objects.
	 *
	 * Token streams have to fulfill a few properties that are assumed by most functions (mostly internal ones) that process
	 * them.
	 *
	 * 1. No adjacent strings.
	 * 2. No empty strings.
	 *
	 *    The only exception here is the token stream that only contains the empty string and nothing else.
	 *
	 * @typedef {Array<string | Token>} TokenStream
	 * @global
	 * @public
	 */

	/**
	 * Converts the given token or token stream to an HTML representation.
	 *
	 * The following hooks will be run:
	 * 1. `wrap`: On each {@link Token}.
	 *
	 * @param {string | Token | TokenStream} o The token or token stream to be converted.
	 * @param {string} language The name of current language.
	 * @returns {string} The HTML representation of the token or token stream.
	 * @memberof Token
	 * @static
	 */
	Token.stringify = function stringify(o, language) {
		if (typeof o == 'string') {
			return o;
		}
		if (Array.isArray(o)) {
			var s = '';
			o.forEach(function (e) {
				s += stringify(e, language);
			});
			return s;
		}

		var env = {
			type: o.type,
			content: stringify(o.content, language),
			tag: 'span',
			classes: ['token', o.type],
			attributes: {},
			language: language
		};

		var aliases = o.alias;
		if (aliases) {
			if (Array.isArray(aliases)) {
				Array.prototype.push.apply(env.classes, aliases);
			} else {
				env.classes.push(aliases);
			}
		}

		_.hooks.run('wrap', env);

		var attributes = '';
		for (var name in env.attributes) {
			attributes += ' ' + name + '="' + (env.attributes[name] || '').replace(/"/g, '&quot;') + '"';
		}

		return '<' + env.tag + ' class="' + env.classes.join(' ') + '"' + attributes + '>' + env.content + '</' + env.tag + '>';
	};

	/**
	 * @param {RegExp} pattern
	 * @param {number} pos
	 * @param {string} text
	 * @param {boolean} lookbehind
	 * @returns {RegExpExecArray | null}
	 */
	function matchPattern(pattern, pos, text, lookbehind) {
		pattern.lastIndex = pos;
		var match = pattern.exec(text);
		if (match && lookbehind && match[1]) {
			// change the match to remove the text matched by the Prism lookbehind group
			var lookbehindLength = match[1].length;
			match.index += lookbehindLength;
			match[0] = match[0].slice(lookbehindLength);
		}
		return match;
	}

	/**
	 * @param {string} text
	 * @param {LinkedList<string | Token>} tokenList
	 * @param {any} grammar
	 * @param {LinkedListNode<string | Token>} startNode
	 * @param {number} startPos
	 * @param {RematchOptions} [rematch]
	 * @returns {void}
	 * @private
	 *
	 * @typedef RematchOptions
	 * @property {string} cause
	 * @property {number} reach
	 */
	function matchGrammar(text, tokenList, grammar, startNode, startPos, rematch) {
		for (var token in grammar) {
			if (!grammar.hasOwnProperty(token) || !grammar[token]) {
				continue;
			}

			var patterns = grammar[token];
			patterns = Array.isArray(patterns) ? patterns : [patterns];

			for (var j = 0; j < patterns.length; ++j) {
				if (rematch && rematch.cause == token + ',' + j) {
					return;
				}

				var patternObj = patterns[j];
				var inside = patternObj.inside;
				var lookbehind = !!patternObj.lookbehind;
				var greedy = !!patternObj.greedy;
				var alias = patternObj.alias;

				if (greedy && !patternObj.pattern.global) {
					// Without the global flag, lastIndex won't work
					var flags = patternObj.pattern.toString().match(/[imsuy]*$/)[0];
					patternObj.pattern = RegExp(patternObj.pattern.source, flags + 'g');
				}

				/** @type {RegExp} */
				var pattern = patternObj.pattern || patternObj;

				for ( // iterate the token list and keep track of the current token/string position
					var currentNode = startNode.next, pos = startPos;
					currentNode !== tokenList.tail;
					pos += currentNode.value.length, currentNode = currentNode.next
				) {

					if (rematch && pos >= rematch.reach) {
						break;
					}

					var str = currentNode.value;

					if (tokenList.length > text.length) {
						// Something went terribly wrong, ABORT, ABORT!
						return;
					}

					if (str instanceof Token) {
						continue;
					}

					var removeCount = 1; // this is the to parameter of removeBetween
					var match;

					if (greedy) {
						match = matchPattern(pattern, pos, text, lookbehind);
						if (!match || match.index >= text.length) {
							break;
						}

						var from = match.index;
						var to = match.index + match[0].length;
						var p = pos;

						// find the node that contains the match
						p += currentNode.value.length;
						while (from >= p) {
							currentNode = currentNode.next;
							p += currentNode.value.length;
						}
						// adjust pos (and p)
						p -= currentNode.value.length;
						pos = p;

						// the current node is a Token, then the match starts inside another Token, which is invalid
						if (currentNode.value instanceof Token) {
							continue;
						}

						// find the last node which is affected by this match
						for (
							var k = currentNode;
							k !== tokenList.tail && (p < to || typeof k.value === 'string');
							k = k.next
						) {
							removeCount++;
							p += k.value.length;
						}
						removeCount--;

						// replace with the new match
						str = text.slice(pos, p);
						match.index -= pos;
					} else {
						match = matchPattern(pattern, 0, str, lookbehind);
						if (!match) {
							continue;
						}
					}

					// eslint-disable-next-line no-redeclare
					var from = match.index;
					var matchStr = match[0];
					var before = str.slice(0, from);
					var after = str.slice(from + matchStr.length);

					var reach = pos + str.length;
					if (rematch && reach > rematch.reach) {
						rematch.reach = reach;
					}

					var removeFrom = currentNode.prev;

					if (before) {
						removeFrom = addAfter(tokenList, removeFrom, before);
						pos += before.length;
					}

					removeRange(tokenList, removeFrom, removeCount);

					var wrapped = new Token(token, inside ? _.tokenize(matchStr, inside) : matchStr, alias, matchStr);
					currentNode = addAfter(tokenList, removeFrom, wrapped);

					if (after) {
						addAfter(tokenList, currentNode, after);
					}

					if (removeCount > 1) {
						// at least one Token object was removed, so we have to do some rematching
						// this can only happen if the current pattern is greedy

						/** @type {RematchOptions} */
						var nestedRematch = {
							cause: token + ',' + j,
							reach: reach
						};
						matchGrammar(text, tokenList, grammar, currentNode.prev, pos, nestedRematch);

						// the reach might have been extended because of the rematching
						if (rematch && nestedRematch.reach > rematch.reach) {
							rematch.reach = nestedRematch.reach;
						}
					}
				}
			}
		}
	}

	/**
	 * @typedef LinkedListNode
	 * @property {T} value
	 * @property {LinkedListNode<T> | null} prev The previous node.
	 * @property {LinkedListNode<T> | null} next The next node.
	 * @template T
	 * @private
	 */

	/**
	 * @template T
	 * @private
	 */
	function LinkedList() {
		/** @type {LinkedListNode<T>} */
		var head = { value: null, prev: null, next: null };
		/** @type {LinkedListNode<T>} */
		var tail = { value: null, prev: head, next: null };
		head.next = tail;

		/** @type {LinkedListNode<T>} */
		this.head = head;
		/** @type {LinkedListNode<T>} */
		this.tail = tail;
		this.length = 0;
	}

	/**
	 * Adds a new node with the given value to the list.
	 *
	 * @param {LinkedList<T>} list
	 * @param {LinkedListNode<T>} node
	 * @param {T} value
	 * @returns {LinkedListNode<T>} The added node.
	 * @template T
	 */
	function addAfter(list, node, value) {
		// assumes that node != list.tail && values.length >= 0
		var next = node.next;

		var newNode = { value: value, prev: node, next: next };
		node.next = newNode;
		next.prev = newNode;
		list.length++;

		return newNode;
	}
	/**
	 * Removes `count` nodes after the given node. The given node will not be removed.
	 *
	 * @param {LinkedList<T>} list
	 * @param {LinkedListNode<T>} node
	 * @param {number} count
	 * @template T
	 */
	function removeRange(list, node, count) {
		var next = node.next;
		for (var i = 0; i < count && next !== list.tail; i++) {
			next = next.next;
		}
		node.next = next;
		next.prev = node;
		list.length -= i;
	}
	/**
	 * @param {LinkedList<T>} list
	 * @returns {T[]}
	 * @template T
	 */
	function toArray(list) {
		var array = [];
		var node = list.head.next;
		while (node !== list.tail) {
			array.push(node.value);
			node = node.next;
		}
		return array;
	}


	if (!_self.document) {
		if (!_self.addEventListener) {
			// in Node.js
			return _;
		}

		if (!_.disableWorkerMessageHandler) {
			// In worker
			_self.addEventListener('message', function (evt) {
				var message = JSON.parse(evt.data);
				var lang = message.language;
				var code = message.code;
				var immediateClose = message.immediateClose;

				_self.postMessage(_.highlight(code, _.languages[lang], lang));
				if (immediateClose) {
					_self.close();
				}
			}, false);
		}

		return _;
	}

	// Get current script and highlight
	var script = _.util.currentScript();

	if (script) {
		_.filename = script.src;

		if (script.hasAttribute('data-manual')) {
			_.manual = true;
		}
	}

	function highlightAutomaticallyCallback() {
		if (!_.manual) {
			_.highlightAll();
		}
	}

	if (!_.manual) {
		// If the document state is "loading", then we'll use DOMContentLoaded.
		// If the document state is "interactive" and the prism.js script is deferred, then we'll also use the
		// DOMContentLoaded event because there might be some plugins or languages which have also been deferred and they
		// might take longer one animation frame to execute which can create a race condition where only some plugins have
		// been loaded when Prism.highlightAll() is executed, depending on how fast resources are loaded.
		// See https://github.com/PrismJS/prism/issues/2102
		var readyState = document.readyState;
		if (readyState === 'loading' || readyState === 'interactive' && script && script.defer) {
			document.addEventListener('DOMContentLoaded', highlightAutomaticallyCallback);
		} else {
			if (window.requestAnimationFrame) {
				window.requestAnimationFrame(highlightAutomaticallyCallback);
			} else {
				window.setTimeout(highlightAutomaticallyCallback, 16);
			}
		}
	}

	return _;

}(_self));

if (typeof module !== 'undefined' && module.exports) {
	module.exports = Prism;
}

// hack for components to work correctly in node.js
if (typeof global !== 'undefined') {
	global.Prism = Prism;
}

// some additional documentation/types

/**
 * The expansion of a simple `RegExp` literal to support additional properties.
 *
 * @typedef GrammarToken
 * @property {RegExp} pattern The regular expression of the token.
 * @property {boolean} [lookbehind=false] If `true`, then the first capturing group of `pattern` will (effectively)
 * behave as a lookbehind group meaning that the captured text will not be part of the matched text of the new token.
 * @property {boolean} [greedy=false] Whether the token is greedy.
 * @property {string|string[]} [alias] An optional alias or list of aliases.
 * @property {Grammar} [inside] The nested grammar of this token.
 *
 * The `inside` grammar will be used to tokenize the text value of each token of this kind.
 *
 * This can be used to make nested and even recursive language definitions.
 *
 * Note: This can cause infinite recursion. Be careful when you embed different languages or even the same language into
 * each another.
 * @global
 * @public
 */

/**
 * @typedef Grammar
 * @type {Object<string, RegExp | GrammarToken | Array<RegExp | GrammarToken>>}
 * @property {Grammar} [rest] An optional grammar object that will be appended to this grammar.
 * @global
 * @public
 */

/**
 * A function which will invoked after an element was successfully highlighted.
 *
 * @callback HighlightCallback
 * @param {Element} element The element successfully highlighted.
 * @returns {void}
 * @global
 * @public
 */

/**
 * @callback HookCallback
 * @param {Object<string, any>} env The environment variables of the hook.
 * @returns {void}
 * @global
 * @public
 */


/* **********************************************
     Begin prism-markup.js
********************************************** */

Prism.languages.markup = {
	'comment': {
		pattern: /<!--(?:(?!<!--)[\s\S])*?-->/,
		greedy: true
	},
	'prolog': {
		pattern: /<\?[\s\S]+?\?>/,
		greedy: true
	},
	'doctype': {
		// https://www.w3.org/TR/xml/#NT-doctypedecl
		pattern: /<!DOCTYPE(?:[^>"'[\]]|"[^"]*"|'[^']*')+(?:\[(?:[^<"'\]]|"[^"]*"|'[^']*'|<(?!!--)|<!--(?:[^-]|-(?!->))*-->)*\]\s*)?>/i,
		greedy: true,
		inside: {
			'internal-subset': {
				pattern: /(^[^\[]*\[)[\s\S]+(?=\]>$)/,
				lookbehind: true,
				greedy: true,
				inside: null // see below
			},
			'string': {
				pattern: /"[^"]*"|'[^']*'/,
				greedy: true
			},
			'punctuation': /^<!|>$|[[\]]/,
			'doctype-tag': /^DOCTYPE/i,
			'name': /[^\s<>'"]+/
		}
	},
	'cdata': {
		pattern: /<!\[CDATA\[[\s\S]*?\]\]>/i,
		greedy: true
	},
	'tag': {
		pattern: /<\/?(?!\d)[^\s>\/=$<%]+(?:\s(?:\s*[^\s>\/=]+(?:\s*=\s*(?:"[^"]*"|'[^']*'|[^\s'">=]+(?=[\s>]))|(?=[\s/>])))+)?\s*\/?>/,
		greedy: true,
		inside: {
			'tag': {
				pattern: /^<\/?[^\s>\/]+/,
				inside: {
					'punctuation': /^<\/?/,
					'namespace': /^[^\s>\/:]+:/
				}
			},
			'special-attr': [],
			'attr-value': {
				pattern: /=\s*(?:"[^"]*"|'[^']*'|[^\s'">=]+)/,
				inside: {
					'punctuation': [
						{
							pattern: /^=/,
							alias: 'attr-equals'
						},
						{
							pattern: /^(\s*)["']|["']$/,
							lookbehind: true
						}
					]
				}
			},
			'punctuation': /\/?>/,
			'attr-name': {
				pattern: /[^\s>\/]+/,
				inside: {
					'namespace': /^[^\s>\/:]+:/
				}
			}

		}
	},
	'entity': [
		{
			pattern: /&[\da-z]{1,8};/i,
			alias: 'named-entity'
		},
		/&#x?[\da-f]{1,8};/i
	]
};

Prism.languages.markup['tag'].inside['attr-value'].inside['entity'] =
	Prism.languages.markup['entity'];
Prism.languages.markup['doctype'].inside['internal-subset'].inside = Prism.languages.markup;

// Plugin to make entity title show the real entity, idea by Roman Komarov
Prism.hooks.add('wrap', function (env) {

	if (env.type === 'entity') {
		env.attributes['title'] = env.content.replace(/&amp;/, '&');
	}
});

Object.defineProperty(Prism.languages.markup.tag, 'addInlined', {
	/**
	 * Adds an inlined language to markup.
	 *
	 * An example of an inlined language is CSS with `<style>` tags.
	 *
	 * @param {string} tagName The name of the tag that contains the inlined language. This name will be treated as
	 * case insensitive.
	 * @param {string} lang The language key.
	 * @example
	 * addInlined('style', 'css');
	 */
	value: function addInlined(tagName, lang) {
		var includedCdataInside = {};
		includedCdataInside['language-' + lang] = {
			pattern: /(^<!\[CDATA\[)[\s\S]+?(?=\]\]>$)/i,
			lookbehind: true,
			inside: Prism.languages[lang]
		};
		includedCdataInside['cdata'] = /^<!\[CDATA\[|\]\]>$/i;

		var inside = {
			'included-cdata': {
				pattern: /<!\[CDATA\[[\s\S]*?\]\]>/i,
				inside: includedCdataInside
			}
		};
		inside['language-' + lang] = {
			pattern: /[\s\S]+/,
			inside: Prism.languages[lang]
		};

		var def = {};
		def[tagName] = {
			pattern: RegExp(/(<__[^>]*>)(?:<!\[CDATA\[(?:[^\]]|\](?!\]>))*\]\]>|(?!<!\[CDATA\[)[\s\S])*?(?=<\/__>)/.source.replace(/__/g, function () { return tagName; }), 'i'),
			lookbehind: true,
			greedy: true,
			inside: inside
		};

		Prism.languages.insertBefore('markup', 'cdata', def);
	}
});
Object.defineProperty(Prism.languages.markup.tag, 'addAttribute', {
	/**
	 * Adds an pattern to highlight languages embedded in HTML attributes.
	 *
	 * An example of an inlined language is CSS with `style` attributes.
	 *
	 * @param {string} attrName The name of the tag that contains the inlined language. This name will be treated as
	 * case insensitive.
	 * @param {string} lang The language key.
	 * @example
	 * addAttribute('style', 'css');
	 */
	value: function (attrName, lang) {
		Prism.languages.markup.tag.inside['special-attr'].push({
			pattern: RegExp(
				/(^|["'\s])/.source + '(?:' + attrName + ')' + /\s*=\s*(?:"[^"]*"|'[^']*'|[^\s'">=]+(?=[\s>]))/.source,
				'i'
			),
			lookbehind: true,
			inside: {
				'attr-name': /^[^\s=]+/,
				'attr-value': {
					pattern: /=[\s\S]+/,
					inside: {
						'value': {
							pattern: /(^=\s*(["']|(?!["'])))\S[\s\S]*(?=\2$)/,
							lookbehind: true,
							alias: [lang, 'language-' + lang],
							inside: Prism.languages[lang]
						},
						'punctuation': [
							{
								pattern: /^=/,
								alias: 'attr-equals'
							},
							/"|'/
						]
					}
				}
			}
		});
	}
});

Prism.languages.html = Prism.languages.markup;
Prism.languages.mathml = Prism.languages.markup;
Prism.languages.svg = Prism.languages.markup;

Prism.languages.xml = Prism.languages.extend('markup', {});
Prism.languages.ssml = Prism.languages.xml;
Prism.languages.atom = Prism.languages.xml;
Prism.languages.rss = Prism.languages.xml;


/* **********************************************
     Begin prism-css.js
********************************************** */

(function (Prism) {

	var string = /(?:"(?:\\(?:\r\n|[\s\S])|[^"\\\r\n])*"|'(?:\\(?:\r\n|[\s\S])|[^'\\\r\n])*')/;

	Prism.languages.css = {
		'comment': /\/\*[\s\S]*?\*\//,
		'atrule': {
			pattern: RegExp('@[\\w-](?:' + /[^;{\s"']|\s+(?!\s)/.source + '|' + string.source + ')*?' + /(?:;|(?=\s*\{))/.source),
			inside: {
				'rule': /^@[\w-]+/,
				'selector-function-argument': {
					pattern: /(\bselector\s*\(\s*(?![\s)]))(?:[^()\s]|\s+(?![\s)])|\((?:[^()]|\([^()]*\))*\))+(?=\s*\))/,
					lookbehind: true,
					alias: 'selector'
				},
				'keyword': {
					pattern: /(^|[^\w-])(?:and|not|only|or)(?![\w-])/,
					lookbehind: true
				}
				// See rest below
			}
		},
		'url': {
			// https://drafts.csswg.org/css-values-3/#urls
			pattern: RegExp('\\burl\\((?:' + string.source + '|' + /(?:[^\\\r\n()"']|\\[\s\S])*/.source + ')\\)', 'i'),
			greedy: true,
			inside: {
				'function': /^url/i,
				'punctuation': /^\(|\)$/,
				'string': {
					pattern: RegExp('^' + string.source + '$'),
					alias: 'url'
				}
			}
		},
		'selector': {
			pattern: RegExp('(^|[{}\\s])[^{}\\s](?:[^{};"\'\\s]|\\s+(?![\\s{])|' + string.source + ')*(?=\\s*\\{)'),
			lookbehind: true
		},
		'string': {
			pattern: string,
			greedy: true
		},
		'property': {
			pattern: /(^|[^-\w\xA0-\uFFFF])(?!\s)[-_a-z\xA0-\uFFFF](?:(?!\s)[-\w\xA0-\uFFFF])*(?=\s*:)/i,
			lookbehind: true
		},
		'important': /!important\b/i,
		'function': {
			pattern: /(^|[^-a-z0-9])[-a-z0-9]+(?=\()/i,
			lookbehind: true
		},
		'punctuation': /[(){};:,]/
	};

	Prism.languages.css['atrule'].inside.rest = Prism.languages.css;

	var markup = Prism.languages.markup;
	if (markup) {
		markup.tag.addInlined('style', 'css');
		markup.tag.addAttribute('style', 'css');
	}

}(Prism));


/* **********************************************
     Begin prism-clike.js
********************************************** */

Prism.languages.clike = {
	'comment': [
		{
			pattern: /(^|[^\\])\/\*[\s\S]*?(?:\*\/|$)/,
			lookbehind: true,
			greedy: true
		},
		{
			pattern: /(^|[^\\:])\/\/.*/,
			lookbehind: true,
			greedy: true
		}
	],
	'string': {
		pattern: /(["'])(?:\\(?:\r\n|[\s\S])|(?!\1)[^\\\r\n])*\1/,
		greedy: true
	},
	'class-name': {
		pattern: /(\b(?:class|extends|implements|instanceof|interface|new|trait)\s+|\bcatch\s+\()[\w.\\]+/i,
		lookbehind: true,
		inside: {
			'punctuation': /[.\\]/
		}
	},
	'keyword': /\b(?:break|catch|continue|do|else|finally|for|function|if|in|instanceof|new|null|return|throw|try|while)\b/,
	'boolean': /\b(?:false|true)\b/,
	'function': /\b\w+(?=\()/,
	'number': /\b0x[\da-f]+\b|(?:\b\d+(?:\.\d*)?|\B\.\d+)(?:e[+-]?\d+)?/i,
	'operator': /[<>]=?|[!=]=?=?|--?|\+\+?|&&?|\|\|?|[?*/~^%]/,
	'punctuation': /[{}[\];(),.:]/
};


/* **********************************************
     Begin prism-javascript.js
********************************************** */

Prism.languages.javascript = Prism.languages.extend('clike', {
	'class-name': [
		Prism.languages.clike['class-name'],
		{
			pattern: /(^|[^$\w\xA0-\uFFFF])(?!\s)[_$A-Z\xA0-\uFFFF](?:(?!\s)[$\w\xA0-\uFFFF])*(?=\.(?:constructor|prototype))/,
			lookbehind: true
		}
	],
	'keyword': [
		{
			pattern: /((?:^|\})\s*)catch\b/,
			lookbehind: true
		},
		{
			pattern: /(^|[^.]|\.\.\.\s*)\b(?:as|assert(?=\s*\{)|async(?=\s*(?:function\b|\(|[$\w\xA0-\uFFFF]|$))|await|break|case|class|const|continue|debugger|default|delete|do|else|enum|export|extends|finally(?=\s*(?:\{|$))|for|from(?=\s*(?:['"]|$))|function|(?:get|set)(?=\s*(?:[#\[$\w\xA0-\uFFFF]|$))|if|implements|import|in|instanceof|interface|let|new|null|of|package|private|protected|public|return|static|super|switch|this|throw|try|typeof|undefined|var|void|while|with|yield)\b/,
			lookbehind: true
		},
	],
	// Allow for all non-ASCII characters (See http://stackoverflow.com/a/2008444)
	'function': /#?(?!\s)[_$a-zA-Z\xA0-\uFFFF](?:(?!\s)[$\w\xA0-\uFFFF])*(?=\s*(?:\.\s*(?:apply|bind|call)\s*)?\()/,
	'number': {
		pattern: RegExp(
			/(^|[^\w$])/.source +
			'(?:' +
			(
				// constant
				/NaN|Infinity/.source +
				'|' +
				// binary integer
				/0[bB][01]+(?:_[01]+)*n?/.source +
				'|' +
				// octal integer
				/0[oO][0-7]+(?:_[0-7]+)*n?/.source +
				'|' +
				// hexadecimal integer
				/0[xX][\dA-Fa-f]+(?:_[\dA-Fa-f]+)*n?/.source +
				'|' +
				// decimal bigint
				/\d+(?:_\d+)*n/.source +
				'|' +
				// decimal number (integer or float) but no bigint
				/(?:\d+(?:_\d+)*(?:\.(?:\d+(?:_\d+)*)?)?|\.\d+(?:_\d+)*)(?:[Ee][+-]?\d+(?:_\d+)*)?/.source
			) +
			')' +
			/(?![\w$])/.source
		),
		lookbehind: true
	},
	'operator': /--|\+\+|\*\*=?|=>|&&=?|\|\|=?|[!=]==|<<=?|>>>?=?|[-+*/%&|^!=<>]=?|\.{3}|\?\?=?|\?\.?|[~:]/
});

Prism.languages.javascript['class-name'][0].pattern = /(\b(?:class|extends|implements|instanceof|interface|new)\s+)[\w.\\]+/;

Prism.languages.insertBefore('javascript', 'keyword', {
	'regex': {
		pattern: RegExp(
			// lookbehind
			// eslint-disable-next-line regexp/no-dupe-characters-character-class
			/((?:^|[^$\w\xA0-\uFFFF."'\])\s]|\b(?:return|yield))\s*)/.source +
			// Regex pattern:
			// There are 2 regex patterns here. The RegExp set notation proposal added support for nested character
			// classes if the `v` flag is present. Unfortunately, nested CCs are both context-free and incompatible
			// with the only syntax, so we have to define 2 different regex patterns.
			/\//.source +
			'(?:' +
			/(?:\[(?:[^\]\\\r\n]|\\.)*\]|\\.|[^/\\\[\r\n])+\/[dgimyus]{0,7}/.source +
			'|' +
			// `v` flag syntax. This supports 3 levels of nested character classes.
			/(?:\[(?:[^[\]\\\r\n]|\\.|\[(?:[^[\]\\\r\n]|\\.|\[(?:[^[\]\\\r\n]|\\.)*\])*\])*\]|\\.|[^/\\\[\r\n])+\/[dgimyus]{0,7}v[dgimyus]{0,7}/.source +
			')' +
			// lookahead
			/(?=(?:\s|\/\*(?:[^*]|\*(?!\/))*\*\/)*(?:$|[\r\n,.;:})\]]|\/\/))/.source
		),
		lookbehind: true,
		greedy: true,
		inside: {
			'regex-source': {
				pattern: /^(\/)[\s\S]+(?=\/[a-z]*$)/,
				lookbehind: true,
				alias: 'language-regex',
				inside: Prism.languages.regex
			},
			'regex-delimiter': /^\/|\/$/,
			'regex-flags': /^[a-z]+$/,
		}
	},
	// This must be declared before keyword because we use "function" inside the look-forward
	'function-variable': {
		pattern: /#?(?!\s)[_$a-zA-Z\xA0-\uFFFF](?:(?!\s)[$\w\xA0-\uFFFF])*(?=\s*[=:]\s*(?:async\s*)?(?:\bfunction\b|(?:\((?:[^()]|\([^()]*\))*\)|(?!\s)[_$a-zA-Z\xA0-\uFFFF](?:(?!\s)[$\w\xA0-\uFFFF])*)\s*=>))/,
		alias: 'function'
	},
	'parameter': [
		{
			pattern: /(function(?:\s+(?!\s)[_$a-zA-Z\xA0-\uFFFF](?:(?!\s)[$\w\xA0-\uFFFF])*)?\s*\(\s*)(?!\s)(?:[^()\s]|\s+(?![\s)])|\([^()]*\))+(?=\s*\))/,
			lookbehind: true,
			inside: Prism.languages.javascript
		},
		{
			pattern: /(^|[^$\w\xA0-\uFFFF])(?!\s)[_$a-z\xA0-\uFFFF](?:(?!\s)[$\w\xA0-\uFFFF])*(?=\s*=>)/i,
			lookbehind: true,
			inside: Prism.languages.javascript
		},
		{
			pattern: /(\(\s*)(?!\s)(?:[^()\s]|\s+(?![\s)])|\([^()]*\))+(?=\s*\)\s*=>)/,
			lookbehind: true,
			inside: Prism.languages.javascript
		},
		{
			pattern: /((?:\b|\s|^)(?!(?:as|async|await|break|case|catch|class|const|continue|debugger|default|delete|do|else|enum|export|extends|finally|for|from|function|get|if|implements|import|in|instanceof|interface|let|new|null|of|package|private|protected|public|return|set|static|super|switch|this|throw|try|typeof|undefined|var|void|while|with|yield)(?![$\w\xA0-\uFFFF]))(?:(?!\s)[_$a-zA-Z\xA0-\uFFFF](?:(?!\s)[$\w\xA0-\uFFFF])*\s*)\(\s*|\]\s*\(\s*)(?!\s)(?:[^()\s]|\s+(?![\s)])|\([^()]*\))+(?=\s*\)\s*\{)/,
			lookbehind: true,
			inside: Prism.languages.javascript
		}
	],
	'constant': /\b[A-Z](?:[A-Z_]|\dx?)*\b/
});

Prism.languages.insertBefore('javascript', 'string', {
	'hashbang': {
		pattern: /^#!.*/,
		greedy: true,
		alias: 'comment'
	},
	'template-string': {
		pattern: /`(?:\\[\s\S]|\$\{(?:[^{}]|\{(?:[^{}]|\{[^}]*\})*\})+\}|(?!\$\{)[^\\`])*`/,
		greedy: true,
		inside: {
			'template-punctuation': {
				pattern: /^`|`$/,
				alias: 'string'
			},
			'interpolation': {
				pattern: /((?:^|[^\\])(?:\\{2})*)\$\{(?:[^{}]|\{(?:[^{}]|\{[^}]*\})*\})+\}/,
				lookbehind: true,
				inside: {
					'interpolation-punctuation': {
						pattern: /^\$\{|\}$/,
						alias: 'punctuation'
					},
					rest: Prism.languages.javascript
				}
			},
			'string': /[\s\S]+/
		}
	},
	'string-property': {
		pattern: /((?:^|[,{])[ \t]*)(["'])(?:\\(?:\r\n|[\s\S])|(?!\2)[^\\\r\n])*\2(?=\s*:)/m,
		lookbehind: true,
		greedy: true,
		alias: 'property'
	}
});

Prism.languages.insertBefore('javascript', 'operator', {
	'literal-property': {
		pattern: /((?:^|[,{])[ \t]*)(?!\s)[_$a-zA-Z\xA0-\uFFFF](?:(?!\s)[$\w\xA0-\uFFFF])*(?=\s*:)/m,
		lookbehind: true,
		alias: 'property'
	},
});

if (Prism.languages.markup) {
	Prism.languages.markup.tag.addInlined('script', 'javascript');

	// add attribute support for all DOM events.
	// https://developer.mozilla.org/en-US/docs/Web/Events#Standard_events
	Prism.languages.markup.tag.addAttribute(
		/on(?:abort|blur|change|click|composition(?:end|start|update)|dblclick|error|focus(?:in|out)?|key(?:down|up)|load|mouse(?:down|enter|leave|move|out|over|up)|reset|resize|scroll|select|slotchange|submit|unload|wheel)/.source,
		'javascript'
	);
}

Prism.languages.js = Prism.languages.javascript;


/* **********************************************
     Begin prism-file-highlight.js
********************************************** */

(function () {

	if (typeof Prism === 'undefined' || typeof document === 'undefined') {
		return;
	}

	// https://developer.mozilla.org/en-US/docs/Web/API/Element/matches#Polyfill
	if (!Element.prototype.matches) {
		Element.prototype.matches = Element.prototype.msMatchesSelector || Element.prototype.webkitMatchesSelector;
	}

	var LOADING_MESSAGE = 'Loading…';
	var FAILURE_MESSAGE = function (status, message) {
		return '✖ Error ' + status + ' while fetching file: ' + message;
	};
	var FAILURE_EMPTY_MESSAGE = '✖ Error: File does not exist or is empty';

	var EXTENSIONS = {
		'js': 'javascript',
		'py': 'python',
		'rb': 'ruby',
		'ps1': 'powershell',
		'psm1': 'powershell',
		'sh': 'bash',
		'bat': 'batch',
		'h': 'c',
		'tex': 'latex'
	};

	var STATUS_ATTR = 'data-src-status';
	var STATUS_LOADING = 'loading';
	var STATUS_LOADED = 'loaded';
	var STATUS_FAILED = 'failed';

	var SELECTOR = 'pre[data-src]:not([' + STATUS_ATTR + '="' + STATUS_LOADED + '"])'
		+ ':not([' + STATUS_ATTR + '="' + STATUS_LOADING + '"])';

	/**
	 * Loads the given file.
	 *
	 * @param {string} src The URL or path of the source file to load.
	 * @param {(result: string) => void} success
	 * @param {(reason: string) => void} error
	 */
	function loadFile(src, success, error) {
		var xhr = new XMLHttpRequest();
		xhr.open('GET', src, true);
		xhr.onreadystatechange = function () {
			if (xhr.readyState == 4) {
				if (xhr.status < 400 && xhr.responseText) {
					success(xhr.responseText);
				} else {
					if (xhr.status >= 400) {
						error(FAILURE_MESSAGE(xhr.status, xhr.statusText));
					} else {
						error(FAILURE_EMPTY_MESSAGE);
					}
				}
			}
		};
		xhr.send(null);
	}

	/**
	 * Parses the given range.
	 *
	 * This returns a range with inclusive ends.
	 *
	 * @param {string | null | undefined} range
	 * @returns {[number, number | undefined] | undefined}
	 */
	function parseRange(range) {
		var m = /^\s*(\d+)\s*(?:(,)\s*(?:(\d+)\s*)?)?$/.exec(range || '');
		if (m) {
			var start = Number(m[1]);
			var comma = m[2];
			var end = m[3];

			if (!comma) {
				return [start, start];
			}
			if (!end) {
				return [start, undefined];
			}
			return [start, Number(end)];
		}
		return undefined;
	}

	Prism.hooks.add('before-highlightall', function (env) {
		env.selector += ', ' + SELECTOR;
	});

	Prism.hooks.add('before-sanity-check', function (env) {
		var pre = /** @type {HTMLPreElement} */ (env.element);
		if (pre.matches(SELECTOR)) {
			env.code = ''; // fast-path the whole thing and go to complete

			pre.setAttribute(STATUS_ATTR, STATUS_LOADING); // mark as loading

			// add code element with loading message
			var code = pre.appendChild(document.createElement('CODE'));
			code.textContent = LOADING_MESSAGE;

			var src = pre.getAttribute('data-src');

			var language = env.language;
			if (language === 'none') {
				// the language might be 'none' because there is no language set;
				// in this case, we want to use the extension as the language
				var extension = (/\.(\w+)$/.exec(src) || [, 'none'])[1];
				language = EXTENSIONS[extension] || extension;
			}

			// set language classes
			Prism.util.setLanguage(code, language);
			Prism.util.setLanguage(pre, language);

			// preload the language
			var autoloader = Prism.plugins.autoloader;
			if (autoloader) {
				autoloader.loadLanguages(language);
			}

			// load file
			loadFile(
				src,
				function (text) {
					// mark as loaded
					pre.setAttribute(STATUS_ATTR, STATUS_LOADED);

					// handle data-range
					var range = parseRange(pre.getAttribute('data-range'));
					if (range) {
						var lines = text.split(/\r\n?|\n/g);

						// the range is one-based and inclusive on both ends
						var start = range[0];
						var end = range[1] == null ? lines.length : range[1];

						if (start < 0) { start += lines.length; }
						start = Math.max(0, Math.min(start - 1, lines.length));
						if (end < 0) { end += lines.length; }
						end = Math.max(0, Math.min(end, lines.length));

						text = lines.slice(start, end).join('\n');

						// add data-start for line numbers
						if (!pre.hasAttribute('data-start')) {
							pre.setAttribute('data-start', String(start + 1));
						}
					}

					// highlight code
					code.textContent = text;
					Prism.highlightElement(code);
				},
				function (error) {
					// mark as failed
					pre.setAttribute(STATUS_ATTR, STATUS_FAILED);

					code.textContent = error;
				}
			);
		}
	});

	Prism.plugins.fileHighlight = {
		/**
		 * Executes the File Highlight plugin for all matching `pre` elements under the given container.
		 *
		 * Note: Elements which are already loaded or currently loading will not be touched by this method.
		 *
		 * @param {ParentNode} [container=document]
		 */
		highlight: function highlight(container) {
			var elements = (container || document).querySelectorAll(SELECTOR);

			for (var i = 0, element; (element = elements[i++]);) {
				Prism.highlightElement(element);
			}
		}
	};

	var logged = false;
	/** @deprecated Use `Prism.plugins.fileHighlight.highlight` instead. */
	Prism.fileHighlight = function () {
		if (!logged) {
			console.warn('Prism.fileHighlight is deprecated. Use `Prism.plugins.fileHighlight.highlight` instead.');
			logged = true;
		}
		Prism.plugins.fileHighlight.highlight.apply(this, arguments);
	};

}());
This page’s CSS code, highlighted with Prism:

@import url(https://fonts.googleapis.com/css?family=Questrial);
@import url(https://fonts.googleapis.com/css?family=Arvo);

/*
 Shared styles
 */

section h1,
#features li strong,
header h2,
footer p {
	font: 100% Rockwell, Arvo, serif;
}

/*
 Styles
 */

* {
	margin: 0;
	padding: 0;
}

body {
	font: 100%/1.5 Questrial, sans-serif;
	tab-size: 4;
	hyphens: auto;
}

a {
	color: inherit;
}

section h1 {
	font-size: 250%;
}

	section section h1 {
		font-size: 150%;
	}

	section h1 code {
		font-style: normal;
	}

	section h1 > a,
	section h2[id] > a {
		text-decoration: none;
	}

	section h1 > a:before,
	section h2[id] > a:before {
		content: '§';
		position: absolute;
		padding: 0 .2em;
		margin-left: -1em;
		border-radius: .2em;
		color: silver;
		text-shadow: 0 1px white;
	}

	section h1 > a:hover:before,
	section h2[id] > a:hover:before {
		color: black;
		background: #f1ad26;
	}

p {
	margin: 1em 0;
}

section h1,
h2,
h3 {
	margin: 1em 0 .3em;
}

h2,
h3 {
	font-weight: normal;

	> a {
		text-decoration: none;
	}
}

dt {
	margin: 1em 0 0 0;
	font-size: 130%;
}

	dt:after {
		content: ':';
	}

dd {
	margin-left: 2em;
}

code, pre {
	font-family: Consolas, Monaco, 'Andale Mono', 'Lucida Console', monospace;
	hyphens: none;
}

pre {
	max-height: 30em;
	overflow: auto;
}

mark {
	outline: .4em solid red;
	outline-offset: .4em;
	margin: .4em 0;
	background-color: transparent;
	display: inline-block;
}

header,
body > main {
	display: block;
	max-width: 900px;
	margin: auto;
}

header, footer {
	position: relative;
	padding: 30px -webkit-calc(50% - 450px); /* Workaround for bug */
	padding: 30px calc(50% - 450px);
	color: white;
	text-shadow: 0 -1px 2px black, 0 0 4px black,
	             0 -1px 0 black, 0 1px 0 black, -1px 0 0 black, 1px 0 0 black;
	background: linear-gradient(transparent, rgba(0, 0, 0, 0.6)), url(img/spectrum.png) fixed;
}

header:before,
footer:before {
	content: '';
	position: absolute;
	bottom: 0; left: 0; right: 0;
	height: 20px;
	background-size: 20px 40px;
	background-repeat: repeat-x;
	background-image: linear-gradient(45deg, transparent 34%, white 34%, white 66%, transparent 66%),
	                  linear-gradient(135deg, transparent 34%, white 34%, white 66%, transparent 66%);
}

	header .intro,
	html.simple header {
		overflow: hidden;
	}

	header h1 {
		float: left;
		margin-right: 30px;
		color: #7fab14;
		text-align: center;
		font-size: 140%;
		text-transform: uppercase;
		letter-spacing: .25em;
	}

	header h2 {
		margin-top: .5em;
		color: #f1ad26;
	}

		header h1 a {
			text-decoration: none;
		}

		header img {
			display: block;
			width: 150px;
			height: 128px;
			margin-bottom: .3em;
			border: 0;
		}

	header h2 {
		font-size: 300%;
	}

	header .intro p {
		margin: 0;
		font: 150%/1.4 Questrial, sans-serif;
		font-size: 150%;
	}

	#features {
		margin-top: 1.6em;
	}

		#features li {
			margin: 0 0 1.6em 0;
			list-style: none;
			display: inline-block;
			width: 49%;
			box-sizing: border-box;
			vertical-align: top;
		}

		#features li:nth-child(odd) {
			padding-right: 2em;
		}
		#features li:nth-child(even) {
			padding-left: 2em;
		}

			#features li:before {
				content: '✓';
				float: left;
				margin-left: -.8em;
				color: #7fab14;
				font-size: 320%;
				line-height: 1;
			}

				#features li strong {
					display: block;
					margin-bottom: .1em;
					font-size: 160%;
				}

	header .download-button {
		float: right;
		margin: 0 0 .5em .5em;
	}

	#theme {
		position: relative;
		z-index: 1;
		float: right;
		margin-right: -9em;
		text-align: center;
		text-transform: uppercase;
		letter-spacing: .2em;
		text-shadow: 0 -1px 2px black;
	}

		#theme > p {
			position: absolute;
			left: 100%;
			transform: translateX(50%) rotate(90deg) ;
			transform-origin: top left;
			font-size: 130%;
		}

		#theme > label {
			position: relative;
			display: flex;
			justify-content: center;
			align-items: center;
			width: 8.5em;
			height: 8.5em;
			line-height: 1em;
			border-radius: 50%;
			background: hsla(0,0%,100%,.5);
			cursor: pointer;
			font-size: 90%;
			padding: 0;
		}

		#theme > label:before {
			content: '';
			position: absolute;
			top: 0; right: 0; bottom: 0; left: 0;
			z-index: -1;
			border-radius: inherit;
			background: url(img/spectrum.png) fixed;
		}

		#theme > label:nth-of-type(n+2) {
			margin-top: -2.5em;
		}

		#theme > input:not(:checked) + label:hover {
			background: hsla(77, 80%, 60%, .5);
		}

		#theme > input {
			position: absolute;
			left: 0;
			clip: rect(0,0,0,0);
		}

		#theme > input:checked + label {
			background: #7fab14;
		}

		@media (max-width: 1300px) and (min-width: 1051px) {
			#theme {
				position: relative;
				z-index: 1;
				float: left;
				margin: 1em 0;
				width: 100%;
			}
			#theme + * {
				clear: both;
			}

				#theme > p {
					margin-top: .5em;
				}

				#theme > label {
					float: left;
					font-size: 82.6%;
				}

				#theme > label:before {
					display: none;
				}

				#theme > label:nth-of-type(n+2) {
					margin-top: 0;
				}
		}

		@media (max-width: 1050px) {
			#theme {
				position: relative;
				z-index: 1;
				float: left;
				margin: 1em 0;
			}
			#theme + * {
				clear: both;
			}

				#theme > p {
					left: inherit;
					right: -1em;
				}

				#theme > label {
					float: left;
				}

				#theme > label:before {
					display: none;
				}

				#theme > label:nth-of-type(n+2) {
					margin-top: 0;
				}
				#theme > label:nth-of-type(n+5) {
					margin-top: -2.5em;
				}
				#theme > label:nth-of-type(4n+1) {
					margin-left: 12.5em;
				}
		}

		@media (max-width: 800px) {
			#theme > label:nth-of-type(4) {
				margin-right: 4em;
			}
			#theme > label:nth-of-type(4n+1) {
				margin-left: 4em;
			}
		}


footer {
	margin-top: 2em;
	background-position: bottom;
	color: white;
}

	footer:before {
		bottom: auto;
		top: 0;
		background-position: bottom;
	}

	footer p {
		font-size: 150%;
	}

	footer ul {
		column-count: 3;
	}

.download-button {
	display: block;
	padding: .2em .8em .1em;
	border: 1px solid rgba(0,0,0,0.5);
	border-radius: 10px;
	background: #39a1cf;
	box-shadow: 0 2px 10px black,
	   inset 0 1px hsla(0,0%,100%,.3),
	   inset 0 .4em hsla(0,0%,100%,.2),
	   inset 0 10px 20px hsla(0,0%,100%,.25),
	   inset 0 -15px 30px rgba(0,0,0,0.3);
	color: white;
	text-shadow: 0 -1px 2px black;
	text-align: center;
	font-size: 250%;
	line-height: 1.5;
	text-transform: uppercase;
	text-decoration: none;
	hyphens: manual;
}

.download-button:hover {
	background-color: #7fab14;
}

.download-button:active {
	box-shadow: inset 0 2px 8px rgba(0,0,0,.8);
}

#toc {
	position: fixed;
	bottom: 15px;
	max-width: calc(50% - 450px - 40px);
	font-size: 80%;
	z-index: 999;
	background: white;
	color: rgba(0,0,0,.5);
	padding: 0 10px 10px;
	border-radius: 0 3px 3px 0;
	box-sizing: border-box;

	&:hover {
		color: rgba(0,0,0,1);
	}

	h2 {
		font-family: Rockwell, Arvo, serif;
		font-size: 180%;
		margin-top: .75rem;
	}

	li {
		list-style: none;
		line-height: 1.2;
		padding: .2em 0;

		a {
			padding: .2em 0;
		}
	}

	> nav.toc {
		li > ul {
			margin-inline-start: .5rem;
		}
	}

	&:has(+ main .back-to-top) {
		position: static;
		max-width: 900px;
		margin-inline: auto;
		font-size: 100%;
		color: black;

		:is([data-inputpath="known-failures.md"] &) > .toc {
			column-count: 4;
	
			li > ul {
				/* Show top-level headings only */
				display: none;
			}
		}
	}

	@media (max-width: 1200px) {
		&:not(:has(+ main .back-to-top)) {
			display: none;
		}
	}
}


#logo {
	float: right;
	margin-block-start: 3.5em;
	margin-inline-start: .5em;
	height: 5em;
	filter: brightness(0) invert(1);
}

.used-by-logos {
	overflow: hidden;
}
	.used-by-logos > a {
		float: left;
		width: 33.33%;
		height: 100px;
		text-align: center;
		background: #F5F2F0;
		box-sizing: border-box;
		border: 5px solid white;
		position: relative;
	}
		.used-by-logos > a > img {
			max-height: 100%;
			max-width: 100%;
			position: absolute;
			top: 50%;
			left: 50%;
			transform: translate(-50%, -50%);
		}

label a.owner {
	margin: 0 .5em;
}

label a.owner:not(:hover) {
	text-decoration: none;
	color: #aaa;
}

#languages-list {
	column-count: 3;
	column-gap: 2em;

	li {
		padding: .2em;
	}
	
	li[data-id="javascript"] {
		border-bottom: 1px solid #aaa;
		padding-bottom: 1em;
		margin-bottom: 1em;
		margin-right: 1em;
	}
}

ul.plugin-list {
	column-count: 2;
	column-gap: 2em;

	> li {
		break-inside: avoid;
		page-break-inside: avoid;

		> a {
			font-size: 110%;
		}

		> div {
			margin-bottom: .5em;
		}
	}
}

[data-inputpath="examples.md"] {
	#languages {
		column-count: 4;

		> h1 {
			margin-top: 0;
			column-span: all;
		}

		label {
			display: block;
			padding: .2em;

			&[data-id="javascript"] {
				border-bottom: 1px solid #aaa;
				padding-bottom: 1em;
				margin-bottom: 1em;
			}
		}

		.unavailable {
			color: #aaa;
		}

		input {
			margin-right: .7em;
		}
	}

	#examples {
		> section {
			display: block;
			margin: auto;
			max-width: 900px;
		}

		h3 {
			margin: 1em 0 0.3em;
		}
	}

	main ul {
		padding-left: 40px;
	}
}

[data-inputpath="known-failures.md"] {
	main h2 {
		font-size: 1.2em;

		&[id] > a::before {
			content: "";
		}
	}
}

[data-inputpath="tokens.md"] {
	table.styled {
		border: 1px solid #ccc;
		border-spacing: 0;

		tr:not(:first-child) > * {
			border-top: 1px solid #ccc;
		}

		tr > *:not(:first-child) {
			border-left: 1px solid #ccc;
		}

		tr:nth-child(2n + 1) {
			background-color: #F8F8F8;
		}

		tr > * {
			padding: .5em .75em;
		}

		tr > th {
			text-align: left;
		}
	}
}

[data-inputpath="extending.md"] {
	ol.indent {
		margin: 1em 0;
		padding-left: 2em;
	}

	table.stylish {
		border-collapse: collapse;

		&, tr, td, th {
			border: 1px solid #CCC;
		}

		td, th {
			padding: .5em .75em;
		}
	
		th, td:first-child {
			background-color: #F8F8F8;
		}
	}
}

[data-inputpath="test.md"] {
	textarea {
		width: 100%;
		height: 10em;
		padding: 1em;
		box-sizing: border-box;
		margin: .5em 0;
		background: #f5f2f0;
		color: black;
		text-shadow: 0 1px white;
		tab-size: 4;
		font: 100% Consolas, Monaco, monospace;
		white-space: pre;
		word-wrap: normal;
		resize: vertical;
	}

	#language {
		column-count: 4;

		label {
			display: block;
			padding: .2em;
		}
	
		label[data-id="javascript"] {
			border-bottom: 1px solid #aaa;
			padding-bottom: 1em;
			margin-bottom: 1em;
		}
	
		input {
			margin-right: 1em;
		}
	
		strong {
			display: block;
			column-span: all;
		}
	}

	pre.show-tokens {
		line-height: calc(1.5em + 12px);
	}

	.show-tokens {
		.token:not(:first-child) {
			margin-left: 1px;
		}

		.token:empty {
			background: red;
		}

		.token:empty::before {
			color: white;
			content: 'empty';
			font-style: italic;
			text-shadow: black 0 0 .3em;
		}
	
		.token {
			border: 1px solid;
			padding: 6px 1px;
		}

		.token > .token {
			padding: 4px 1px;
		}

		.token > .token > .token {
			padding: 2px 1px;
		}

		.token > .token > .token > .token {
			padding: 0 1px;
		}

		.token > .token > .token > .token > .token {
			border: none;
			border-left: 1px solid;
			border-right: 1px solid;
			padding: 0;
			margin: 0 1px;
		}
	}

	#options {
		position: relative;
	}

	.link-wrapper {
		position: absolute;
		top: 0;
		right: 0;
		background-color: white;

		.hidden-wrapper {
			display: none;
		}
	
		&:hover {
			top: -.5em;
			right: -1em;
			width: 300px;
			padding: .5em 1em;
			outline: 1px solid #888;

			.hidden-wrapper {
				display: block;
			}
		}
	
		input {
			width: 100%;
			box-sizing: border-box;
		}
	
		button {
			border: none;
			background: none;
			font: inherit;
			text-decoration: underline;
			cursor: pointer;
		}
	}
}

[data-inputpath^="download"] {
	form label {
		display: block;
		padding: .2em;
		padding-left: 1.9em;
		page-break-inside: avoid;
		break-inside: avoid;

		.name {
			margin-right: .3em;
		}
		
		a.owner {
			margin-left: 0;
			hyphens: none;
		}
		
		input {
			margin-right: .7em;
			margin-left: -1.7em;
		}
	}


	form > p:first-of-type > label {
		font-size: 150%;

		input {
			vertical-align: .3em;
		}
	}


	.filesize:empty {
		display: none;
	}

	#components {
		overflow: hidden;
	}

	section.options {
		width: 33.3%;
		width: calc(100% / 3);
		float: left;

		label[data-id^="check-all-"] {
			border-bottom: 1px solid #aaa;
			padding-bottom: 1em;
			margin-bottom: 1em;
		}
	}

	section.options#category-languages,
	section.options#category-plugins {
		width: 100%;
		float: none;
		column-count: 3;
		padding-top: 2em;
		overflow: visible;

		label {
			break-inside: avoid;
		}

		> h1 {
			margin-top: 0;
			column-span: all;
		}
		
		label[data-id="javascript"] {
			border-bottom: 1px solid #aaa;
			padding-bottom: 1em;
			margin-bottom: 1em;
		}
	}


	section.options#category-themes {
		width: 66.6%;
		width: calc(100% * 2/3);
		column-count: 2;
		overflow: visible;
		float: none;

		> h1 {
			column-span: all;
		}
	}


	section.download {
		width: 50%;
		float: left;
	}

	#download {
		overflow: hidden;
		padding: .3em;
		.error {
			color: #B61500;
			display: none;
		}
		pre {
			height: 20em;
		}
		.download-button {
			cursor: pointer;
			width: 100%;
		}

	}

	#download-js .download-button {
		border-top-right-radius: 0;
		border-bottom-right-radius: 0;
	}

	#download-css .download-button {
		background-color: #dc9e23;
		border-top-left-radius: 0;
		border-bottom-left-radius: 0;
	}
}

/* Decorate only the first link in headings */
section :is(h1, h2, h3)[id] {
	a:not(:first-of-type) {
		text-decoration: revert;

		&::before {
			display: none;
		}
	}
}
This page’s HTML, highlighted with Prism:

<!DOCTYPE html>
<html lang="en"
	data-page="/"
	data-inputpath="README.md">
<head>
	<title>
		 Prism 
	</title>
	<meta name="viewport" content="width=device-width" />
	<meta charset="utf-8" />
	<link rel="icon" href="/assets/logo.svg" />
	<link rel="stylesheet" href="/assets/style.css" />
	<link rel="stylesheet" href="https://dev.prismjs.com/themes/prism.css" />
	<script>var _gaq = [["_setAccount", "UA-33746269-1"], ["_trackPageview"]];</script>
	<script src="https://www.google-analytics.com/ga.js" async></script>

	<script>
	// Just a lil’ script to show off that inline JS gets highlighted
	console.log("foo");
</script></head>

<body class="">
	<header>
		<div class="intro">
			<h1><a href="/"><img src="/assets/logo.svg" alt="Prism" /> </a></h1>

			<a class='download-button' href='/download'>Download</a>

			<p>
				Prism is a lightweight, extensible syntax highlighter, built with modern web standards in mind.
				It’s used in millions of websites, including some of those you visit daily.
			</p>
		</div>

		<div id="theme">
			<p>Theme:</p>
			<input type="radio" name="theme" id="theme-prism" value="prism" />
			<label for="theme-prism">Default</label>
			<input type="radio" name="theme" id="theme-prism-dark" value="prism-dark" />
			<label for="theme-prism-dark">Dark</label>
			<input type="radio" name="theme" id="theme-prism-funky" value="prism-funky" />
			<label for="theme-prism-funky">Funky</label>
			<input type="radio" name="theme" id="theme-prism-okaidia" value="prism-okaidia" />
			<label for="theme-prism-okaidia">Okaidia</label>
			<input type="radio" name="theme" id="theme-prism-twilight" value="prism-twilight" />
			<label for="theme-prism-twilight">Twilight</label>
			<input type="radio" name="theme" id="theme-prism-coy" value="prism-coy" />
			<label for="theme-prism-coy">Coy</label>
			<input type="radio" name="theme" id="theme-prism-solarizedlight" value="prism-solarizedlight" />
			<label for="theme-prism-solarizedlight">Solarized Light</label>
			<input type="radio" name="theme" id="theme-prism-tomorrow" value="prism-tomorrow" />
			<label for="theme-prism-tomorrow">Tomorrow Night</label>
		</div>

		<ul id="features">
<li><strong>Dead simple</strong>
Include prism.css and prism.js, use proper HTML5 code tags (<code>code.language-xxxx</code>), done!</li>
<li><strong>Intuitive</strong>
Language classes are inherited so you can only define the language once for multiple code snippets.</li>
<li><strong>Light as a feather</strong>
The core is 2KB minified &amp; gzipped. Languages add 0.3-0.5KB each, themes are around 1KB.</li>
<li><strong>Blazing fast</strong>
Supports parallelism with Web Workers, if available.</li>
<li><strong>Extensible</strong>
Define new languages or extend existing ones. Add new features thanks to Prism’s plugin architecture.</li>
<li><strong>Easy styling</strong>
All styling is done through CSS, with sensible class names like <code>.comment</code>, <code>.string</code>, <code>.property</code> etc.</li>
</ul>

	</header>

	<aside id="toc">
		<h2>On this page</h2>
		<nav class="toc" >
        <ul><li><a href="#used-by">Used By</a></li><li><a href="#examples">Examples</a></li><li><a href="#full-list-of-features">Full list of features</a></li><li><a href="#limitations">Limitations</a></li><li><a href="#basic-usage">Basic usage</a><ul><li><a href="#language-inheritance">Language inheritance</a></li><li><a href="#manual-highlighting">Manual highlighting</a></li><li><a href="#basic-usage-cdn">Usage with CDNs</a></li><li><a href="#basic-usage-bundlers">Usage with Webpack, Browserify, &amp; Other Bundlers</a></li><li><a href="#basic-usage-node">Usage with Node</a></li></ul></li><li><a href="#supported-languages">Supported languages</a></li><li><a href="#plugins">Plugins</a></li><li><a href="#third-party-language-definitions">Third-party language definitions</a></li><li><a href="#third-party-tutorials">Third-party tutorials</a></li><li><a href="#credits">Credits</a></li></ul>
      </nav>
	</aside>

	<main>
		<section>
<h1 id="used-by" tabindex="-1"><a class="header-anchor" href="#used-by">Used By</a></h1>
<p>Prism is used on several websites, small and large. Some of them are:</p>
<div class="used-by-logos">
	<a href="https://www.smashingmagazine.com/" target="_blank"><img src="assets/img/logo-smashing.png" alt="Smashing Magazine" /></a>
	<a href="https://alistapart.com/" target="_blank"><img src="assets/img/logo-ala.png" alt="A List Apart" /></a>
	<a href="https://developer.mozilla.org/" target="_blank"><img src="assets/img/logo-mdn.png" alt="Mozilla Developer Network (MDN)" /></a>
	<a href="https://css-tricks.com/" target="_blank"><img src="assets/img/logo-css-tricks.png" alt="CSS-Tricks" /></a>
	<a href="https://www.sitepoint.com/" target="_blank"><img src="assets/img/logo-sitepoint.png" alt="SitePoint" /></a>
	<a href="https://www.drupal.org/" target="_blank"><img src="assets/img/logo-drupal.png" alt="Drupal" /></a>
	<a href="https://reactjs.org/" target="_blank"><img src="assets/img/logo-react.png" alt="React" /></a>
	<a href="https://stripe.com/" target="_blank"><img src="assets/img/logo-stripe.png" alt="Stripe" /></a>
	<a href="https://dev.mysql.com/" target="_blank"><img src="assets/img/logo-mysql.png" alt="MySQL" /></a>
</div>
</section>
<section>
<h1 id="examples" tabindex="-1"><a class="header-anchor" href="#examples">Examples</a></h1>
<p>The Prism source, highlighted with Prism (don’t you just love how meta this is?):</p>
<pre data-src="https://dev.prismjs.com/prism.js"></pre>
<p>This page’s CSS code, highlighted with Prism:</p>
<pre data-src="assets/style.css"></pre>
<p>This page’s HTML, highlighted with Prism:</p>
<pre data-src="index.html"></pre>
<p>This page’s logo (SVG), highlighted with Prism:</p>
<pre data-src="assets/logo.svg"></pre>
<p>If you’re still not sold, you can <a href='/examples'>view more examples</a> or <a href='/test'>try it out for yourself</a>.</p>
</section>
<section class="language-markup">
<h1 id="full-list-of-features" tabindex="-1"><a class="header-anchor" href="#full-list-of-features">Full list of features</a></h1>
<ul>
<li><strong>Only 2KB</strong> minified &amp; gzipped (core). Each language definition adds roughly 300-500 bytes.</li>
<li>Encourages good author practices. Other highlighters encourage or even force you to use elements that are semantically wrong, like <code>&lt;pre&gt;</code> (on its own) or <code>&lt;script&gt;</code>. Prism forces you to use the correct element for marking up code: <code>&lt;code&gt;</code>. On its own for inline code, or inside a <code>&lt;pre&gt;</code> for blocks of code. In addition, the language is defined through the way recommended in the HTML5 draft: through a <code>language-xxxx</code> class.</li>
<li>The <code>language-xxxx</code> class is inherited. This means that if multiple code snippets have the same language, you can just define it once, in one of their common ancestors.</li>
<li>Supports <strong>parallelism with Web Workers</strong>, if available. Disabled by default (<a href='/faq#why-is-asynchronous-highlighting-disabled-by-default'>why?</a>).</li>
<li>Very easy to extend without modifying the code, due to Prism’s <a href="#plugins">plugin architecture</a>. Multiple hooks are scattered throughout the source.</li>
<li>Very easy to <a href='/extending#language-definitions'>define new languages</a>. The only thing you need is a good understanding of regular expressions.</li>
<li>All styling is done through CSS, with <a href='/faq#how-do-i-know-which-tokens-i-can-style-for'>sensible class names</a> rather than ugly, namespaced, abbreviated nonsense.</li>
<li>Wide browser support: Edge, IE11, Firefox, Chrome, Safari, <a href='/faq#this-page-doesnt-work-in-opera'>Opera</a>, most mobile browsers.</li>
<li>Highlights embedded languages (e.g. CSS inside HTML, JavaScript inside HTML).</li>
<li>Highlights inline code as well, not just code blocks.</li>
<li>It doesn’t force you to use any Prism-specific markup, not even a Prism-specific class name, only standard markup you should be using anyway. So, you can just try it for a while, remove it if you don’t like it and leave no traces behind.</li>
<li>Highlight specific lines and/or line ranges (requires <a href="plugins/line-highlight/">plugin</a>).</li>
<li>Show invisible characters like tabs, line breaks etc (requires <a href="plugins/show-invisibles/">plugin</a>).</li>
<li>Autolink URLs and emails, use Markdown links in comments (requires <a href="plugins/autolinker/">plugin</a>).</li>
</ul>
</section>
<section>
<h1 id="limitations" tabindex="-1"><a class="header-anchor" href="#limitations">Limitations</a></h1>
<ul>
<li>Any pre-existing HTML in the code will be stripped off. <a href='/faq#if-pre-existing-html-is-stripped-off-how-can-i-highlight'>There are ways around it though</a>.</li>
<li>Regex-based so it *will* fail on certain edge cases, which are documented in the <a href='/known-failures'>known failures page</a>.</li>
<li>Some of our themes have problems with certain layouts. Known cases are documented <a href='/known-failures#themes'>here</a>.</li>
<li>No IE 6-10 support. If someone can read code, they are probably in the 95% of the population with a modern browser.</li>
</ul>
</section>
<section class="language-markup">
<h1 id="basic-usage" tabindex="-1"><a class="header-anchor" href="#basic-usage">Basic usage</a></h1>
<p>You will need to include the <code>prism.css</code> and <code>prism.js</code> files you <a href='/download'>downloaded</a> in your page. Example:</p>
<pre><code>&lt;!DOCTYPE html>
&lt;html>
&lt;head>
	...
	<mark>&lt;link href="themes/prism.css" rel="stylesheet" /></mark>
&lt;/head>
&lt;body>
	...
	<mark>&lt;script src="prism.js">&lt;/script></mark>
&lt;/body>
&lt;/html></code></pre>
<p>Prism does its best to encourage good authoring practices. Therefore, it only works with <code>&lt;code&gt;</code> elements, since marking up code without a <code>&lt;code&gt;</code> element is semantically invalid. <a href="https://www.w3.org/TR/html52/textlevel-semantics.html#the-code-element">According to the HTML5 spec</a>, the recommended way to define a code language is a <code>language-xxxx</code> class, which is what Prism uses. Alternatively, Prism also supports a shorter version: <code>lang-xxxx</code>.</p>
<p>The <a href="https://www.w3.org/TR/html5/grouping-content.html#the-pre-element">recommended way to mark up a code block</a> (both for semantics and for Prism) is a <code>&lt;pre&gt;</code> element with a <code>&lt;code&gt;</code> element inside, like so:</p>
<pre ><code class="language-html">&lt;pre&gt;&lt;code class=&quot;language-css&quot;&gt;p { color: red }&lt;/code&gt;&lt;/pre&gt;</code></pre><p>If you use that pattern, the <code>&lt;pre&gt;</code> will automatically get the <code>language-xxxx</code> class (if it doesn’t already have it) and will be styled as a code block.</p>
<p>Inline code snippets are done like this:</p>
<pre ><code class="language-html">&lt;code class=&quot;language-css&quot;&gt;p { color: red }&lt;/code&gt;</code></pre><p><strong>Note</strong>: You have to escape all <code>&lt;</code> and <code>&amp;</code> characters inside <code>&lt;code&gt;</code> elements (code blocks and inline snippets) with <code>&amp;lt;</code> and <code>&amp;amp;</code> respectively, or else the browser might interpret them as an HTML tag or <a href="https://developer.mozilla.org/en-US/docs/Glossary/Entity">entity</a>. If you have large portions of HTML code, you can use the <a href="plugins/unescaped-markup/">Unescaped Markup plugin</a> to work around this.</p>
<h2 id="language-inheritance" tabindex="-1"><a class="header-anchor" href="#language-inheritance">Language inheritance</a></h2>
<p>To make things easier however, Prism assumes that the language class is inherited. Therefore, if multiple <code>&lt;code&gt;</code> elements have the same language, you can add the <code>language-xxxx</code> class on one of their common ancestors. This way, you can also define a document-wide default language, by adding a <code>language-xxxx</code> class on the <code>&lt;body&gt;</code> or <code>&lt;html&gt;</code> element.</p>
<p>If you want to opt-out of highlighting a <code>&lt;code&gt;</code> element that inherits its language, you can add the <code>language-none</code> class to it. The <code>none</code> language can also be inherited to disable highlighting for the element with the class and all of its descendants.</p>
<p>If you want to opt-out of highlighting but still use plugins like <a href="plugins/show-invisibles/">Show Invisibles</a>, use <code>language-plain</code> class instead.</p>
<h2 id="manual-highlighting" tabindex="-1"><a class="header-anchor" href="#manual-highlighting">Manual highlighting</a></h2>
<p>If you want to prevent any elements from being automatically highlighted and instead use the <a href='/extending#api-documentation'>API</a>, you can set <a href='/docs/prism#.manual'><code class="language-javascript">Prism.manual</code></a> to <code class="language-javascript">true</code> before the <code>DOMContentLoaded</code> event is fired. By setting the <code>data-manual</code> attribute on the <code>&lt;script&gt;</code> element containing Prism core, this will be done automatically. Example:</p>
<pre ><code class="language-html">&lt;script src=&quot;prism.js&quot; data-manual&gt;&lt;/script&gt;</code></pre><p>or</p>
<pre ><code class="language-html">&lt;script&gt;
window.Prism = window.Prism || {};
window.Prism.manual = true;
&lt;/script&gt;
&lt;script src=&quot;prism.js&quot;&gt;&lt;/script&gt;</code></pre><h2 id="basic-usage-cdn" tabindex="-1"><a class="header-anchor" href="#basic-usage-cdn">Usage with CDNs</a></h2>
<p>In combination with CDNs, we recommend using the <a href="plugins/autoloader">Autoloader plugin</a> which automatically loads languages when necessary.</p>
<p>The setup of the Autoloader, will look like the following. You can also add your own themes of course.</p>
<pre><code>&lt;!DOCTYPE html>
&lt;html>
&lt;head>
	...
	<mark>&lt;link href="https:///prismjs@v1.x/themes/prism.css" rel="stylesheet" /></mark>
&lt;/head>
&lt;body>
	...
	<mark>&lt;script src="https:///prismjs@v1.x/components/prism-core.min.js"&gt;&lt;/script&gt;
&lt;script src="https:///prismjs@v1.x/plugins/autoloader/prism-autoloader.min.js"&gt;&lt;/script&gt;</mark>
&lt;/body>
&lt;/html></code></pre>
<p>Please note that links in the above code sample serve as placeholders. You have to replace them with valid links to the CDN of your choice.</p>
<p>CDNs which provide PrismJS are e.g. <a href="https://cdnjs.com/libraries/prism">cdnjs</a>, <a href="https://www.jsdelivr.com/package/npm/prismjs">jsDelivr</a>, and <a href="https://unpkg.com/browse/prismjs@1/">UNPKG</a>.</p>
<h2 id="basic-usage-bundlers" tabindex="-1"><a class="header-anchor" href="#basic-usage-bundlers">Usage with Webpack, Browserify, &amp; Other Bundlers</a></h2>
<p>If you want to use Prism with a bundler, install Prism with <code>npm</code>:</p>
<pre ><code class="language-bash">$ npm install prismjs</code></pre><p>You can then <code>import</code> into your bundle:</p>
<pre ><code class="language-js">import Prism from 'prismjs';</code></pre><p>To make it easy to configure your Prism instance with only the languages and plugins you need, use the babel plugin, <a href="https://github.com/mAAdhaTTah/babel-plugin-prismjs">babel-plugin-prismjs</a>. This will allow you to load the minimum number of languages and plugins to satisfy your needs. See that plugin’s documentation for configuration details.</p>
<h2 id="basic-usage-node" tabindex="-1"><a class="header-anchor" href="#basic-usage-node">Usage with Node</a></h2>
<p>If you want to use Prism on the server or through the command line, Prism can be used with Node.js as well. This might be useful if you’re trying to generate static HTML pages with highlighted code for environments that don’t support browser-side JS, like <a href="https://www.ampproject.org/">AMP pages</a>.</p>
<p>Example:</p>
<pre ><code class="language-js">const Prism = require('prismjs');

// The code snippet you want to highlight, as a string
const code = `var data = 1;`;

// Returns a highlighted HTML string
const html = Prism.highlight(code, Prism.languages.javascript, 'javascript');</code></pre><p>Requiring <code>prismjs</code> will load the default languages: <code>markup</code>, <code>css</code>, <code>clike</code> and <code>javascript</code>. You can load more languages with the <code class="language-javascript">loadLanguages()</code> utility, which will automatically handle any required dependencies.</p>
<p>Example:</p>
<pre ><code class="language-js">const Prism = require('prismjs');
const loadLanguages = require('prismjs/components/');
loadLanguages(['haml']);

// The code snippet you want to highlight, as a string
const code = `= ['hi', 'there', 'reader!'].join &quot; &quot;`;

// Returns a highlighted HTML string
const html = Prism.highlight(code, Prism.languages.haml, 'haml');</code></pre><p><strong>Note</strong>: Do <em>not</em> use <code class="language-javascript">loadLanguages()</code> with Webpack or another bundler, as this will cause Webpack to include all languages and plugins. Use the babel plugin described above.</p>
<p><strong>Note</strong>: <code class="language-javascript">loadLanguages()</code> will ignore unknown languages and log warning messages to the console. You can prevent the warnings by setting <code class="language-javascript">loadLanguages.silent = true</code>.</p>
</section>
<section class="language-markup">
<h1 id="supported-languages" tabindex="-1"><a class="header-anchor" href="#supported-languages">Supported languages</a></h1>
<p>This is the list of all 297 languages currently supported by Prism, with their corresponding alias, to use in place of <code>xxxx</code> in the <code>language-xxxx</code> (or <code>lang-xxxx</code>) class:</p>
<ul id="languages-list">
	<li data-id="markup">
		Markup&nbsp;—<code>markup</code>, <code>html</code>, <code>xml</code>, <code>svg</code>, <code>mathml</code>, <code>ssml</code>, <code>atom</code>, <code>rss</code>
	</li>
	<li data-id="css">
		CSS&nbsp;—<code>css</code>
	</li>
	<li data-id="clike">
		C-like&nbsp;—<code>clike</code>
	</li>
	<li data-id="javascript">
		JavaScript&nbsp;—<code>javascript</code>, <code>js</code>
	</li>
	<li data-id="abap">
		ABAP&nbsp;—<code>abap</code>
	</li>
	<li data-id="abnf">
		ABNF&nbsp;—<code>abnf</code>
	</li>
	<li data-id="actionscript">
		ActionScript&nbsp;—<code>actionscript</code>
	</li>
	<li data-id="ada">
		Ada&nbsp;—<code>ada</code>
	</li>
	<li data-id="agda">
		Agda&nbsp;—<code>agda</code>
	</li>
	<li data-id="al">
		AL&nbsp;—<code>al</code>
	</li>
	<li data-id="antlr4">
		ANTLR4&nbsp;—<code>antlr4</code>, <code>g4</code>
	</li>
	<li data-id="apacheconf">
		Apache Configuration&nbsp;—<code>apacheconf</code>
	</li>
	<li data-id="apex">
		Apex&nbsp;—<code>apex</code>
	</li>
	<li data-id="apl">
		APL&nbsp;—<code>apl</code>
	</li>
	<li data-id="applescript">
		AppleScript&nbsp;—<code>applescript</code>
	</li>
	<li data-id="aql">
		AQL&nbsp;—<code>aql</code>
	</li>
	<li data-id="arduino">
		Arduino&nbsp;—<code>arduino</code>, <code>ino</code>
	</li>
	<li data-id="arff">
		ARFF&nbsp;—<code>arff</code>
	</li>
	<li data-id="armasm">
		ARM Assembly&nbsp;—<code>armasm</code>, <code>arm-asm</code>
	</li>
	<li data-id="arturo">
		Arturo&nbsp;—<code>arturo</code>, <code>art</code>
	</li>
	<li data-id="asciidoc">
		AsciiDoc&nbsp;—<code>asciidoc</code>, <code>adoc</code>
	</li>
	<li data-id="aspnet">
		ASP.NET (C#)&nbsp;—<code>aspnet</code>
	</li>
	<li data-id="asm6502">
		6502 Assembly&nbsp;—<code>asm6502</code>
	</li>
	<li data-id="asmatmel">
		Atmel AVR Assembly&nbsp;—<code>asmatmel</code>
	</li>
	<li data-id="autohotkey">
		AutoHotkey&nbsp;—<code>autohotkey</code>
	</li>
	<li data-id="autoit">
		AutoIt&nbsp;—<code>autoit</code>
	</li>
	<li data-id="avisynth">
		AviSynth&nbsp;—<code>avisynth</code>, <code>avs</code>
	</li>
	<li data-id="avro-idl">
		Avro IDL&nbsp;—<code>avro-idl</code>, <code>avdl</code>
	</li>
	<li data-id="awk">
		AWK&nbsp;—<code>awk</code>, <code>gawk</code>
	</li>
	<li data-id="bash">
		Bash&nbsp;—<code>bash</code>, <code>sh</code>, <code>shell</code>
	</li>
	<li data-id="basic">
		BASIC&nbsp;—<code>basic</code>
	</li>
	<li data-id="batch">
		Batch&nbsp;—<code>batch</code>
	</li>
	<li data-id="bbcode">
		BBcode&nbsp;—<code>bbcode</code>, <code>shortcode</code>
	</li>
	<li data-id="bbj">
		BBj&nbsp;—<code>bbj</code>
	</li>
	<li data-id="bicep">
		Bicep&nbsp;—<code>bicep</code>
	</li>
	<li data-id="birb">
		Birb&nbsp;—<code>birb</code>
	</li>
	<li data-id="bison">
		Bison&nbsp;—<code>bison</code>
	</li>
	<li data-id="bnf">
		BNF&nbsp;—<code>bnf</code>, <code>rbnf</code>
	</li>
	<li data-id="bqn">
		BQN&nbsp;—<code>bqn</code>
	</li>
	<li data-id="brainfuck">
		Brainfuck&nbsp;—<code>brainfuck</code>
	</li>
	<li data-id="brightscript">
		BrightScript&nbsp;—<code>brightscript</code>
	</li>
	<li data-id="bro">
		Bro&nbsp;—<code>bro</code>
	</li>
	<li data-id="bsl">
		BSL (1C:Enterprise)&nbsp;—<code>bsl</code>, <code>oscript</code>
	</li>
	<li data-id="c">
		C&nbsp;—<code>c</code>
	</li>
	<li data-id="csharp">
		C#&nbsp;—<code>csharp</code>, <code>cs</code>, <code>dotnet</code>
	</li>
	<li data-id="cpp">
		C++&nbsp;—<code>cpp</code>
	</li>
	<li data-id="cfscript">
		CFScript&nbsp;—<code>cfscript</code>, <code>cfc</code>
	</li>
	<li data-id="chaiscript">
		ChaiScript&nbsp;—<code>chaiscript</code>
	</li>
	<li data-id="cil">
		CIL&nbsp;—<code>cil</code>
	</li>
	<li data-id="cilkc">
		Cilk/C&nbsp;—<code>cilkc</code>, <code>cilk-c</code>
	</li>
	<li data-id="cilkcpp">
		Cilk/C++&nbsp;—<code>cilkcpp</code>, <code>cilk-cpp</code>, <code>cilk</code>
	</li>
	<li data-id="clojure">
		Clojure&nbsp;—<code>clojure</code>
	</li>
	<li data-id="cmake">
		CMake&nbsp;—<code>cmake</code>
	</li>
	<li data-id="cobol">
		COBOL&nbsp;—<code>cobol</code>
	</li>
	<li data-id="coffeescript">
		CoffeeScript&nbsp;—<code>coffeescript</code>, <code>coffee</code>
	</li>
	<li data-id="concurnas">
		Concurnas&nbsp;—<code>concurnas</code>, <code>conc</code>
	</li>
	<li data-id="csp">
		Content-Security-Policy&nbsp;—<code>csp</code>
	</li>
	<li data-id="cooklang">
		Cooklang&nbsp;—<code>cooklang</code>
	</li>
	<li data-id="coq">
		Coq&nbsp;—<code>coq</code>
	</li>
	<li data-id="crystal">
		Crystal&nbsp;—<code>crystal</code>
	</li>
	<li data-id="css-extras">
		CSS Extras&nbsp;—<code>css-extras</code>
	</li>
	<li data-id="csv">
		CSV&nbsp;—<code>csv</code>
	</li>
	<li data-id="cue">
		CUE&nbsp;—<code>cue</code>
	</li>
	<li data-id="cypher">
		Cypher&nbsp;—<code>cypher</code>
	</li>
	<li data-id="d">
		D&nbsp;—<code>d</code>
	</li>
	<li data-id="dart">
		Dart&nbsp;—<code>dart</code>
	</li>
	<li data-id="dataweave">
		DataWeave&nbsp;—<code>dataweave</code>
	</li>
	<li data-id="dax">
		DAX&nbsp;—<code>dax</code>
	</li>
	<li data-id="dhall">
		Dhall&nbsp;—<code>dhall</code>
	</li>
	<li data-id="diff">
		Diff&nbsp;—<code>diff</code>
	</li>
	<li data-id="django">
		Django/Jinja2&nbsp;—<code>django</code>, <code>jinja2</code>
	</li>
	<li data-id="dns-zone-file">
		DNS zone file&nbsp;—<code>dns-zone-file</code>, <code>dns-zone</code>
	</li>
	<li data-id="docker">
		Docker&nbsp;—<code>docker</code>, <code>dockerfile</code>
	</li>
	<li data-id="dot">
		DOT (Graphviz)&nbsp;—<code>dot</code>, <code>gv</code>
	</li>
	<li data-id="ebnf">
		EBNF&nbsp;—<code>ebnf</code>
	</li>
	<li data-id="editorconfig">
		EditorConfig&nbsp;—<code>editorconfig</code>
	</li>
	<li data-id="eiffel">
		Eiffel&nbsp;—<code>eiffel</code>
	</li>
	<li data-id="ejs">
		EJS&nbsp;—<code>ejs</code>, <code>eta</code>
	</li>
	<li data-id="elixir">
		Elixir&nbsp;—<code>elixir</code>
	</li>
	<li data-id="elm">
		Elm&nbsp;—<code>elm</code>
	</li>
	<li data-id="etlua">
		Embedded Lua templating&nbsp;—<code>etlua</code>
	</li>
	<li data-id="erb">
		ERB&nbsp;—<code>erb</code>
	</li>
	<li data-id="erlang">
		Erlang&nbsp;—<code>erlang</code>
	</li>
	<li data-id="excel-formula">
		Excel Formula&nbsp;—<code>excel-formula</code>, <code>xlsx</code>, <code>xls</code>
	</li>
	<li data-id="fsharp">
		F#&nbsp;—<code>fsharp</code>
	</li>
	<li data-id="factor">
		Factor&nbsp;—<code>factor</code>
	</li>
	<li data-id="false">
		False&nbsp;—<code>false</code>
	</li>
	<li data-id="firestore-security-rules">
		Firestore security rules&nbsp;—<code>firestore-security-rules</code>
	</li>
	<li data-id="flow">
		Flow&nbsp;—<code>flow</code>
	</li>
	<li data-id="fortran">
		Fortran&nbsp;—<code>fortran</code>
	</li>
	<li data-id="ftl">
		FreeMarker Template Language&nbsp;—<code>ftl</code>
	</li>
	<li data-id="gml">
		GameMaker Language&nbsp;—<code>gml</code>, <code>gamemakerlanguage</code>
	</li>
	<li data-id="gap">
		GAP (CAS)&nbsp;—<code>gap</code>
	</li>
	<li data-id="gcode">
		G-code&nbsp;—<code>gcode</code>
	</li>
	<li data-id="gdscript">
		GDScript&nbsp;—<code>gdscript</code>
	</li>
	<li data-id="gedcom">
		GEDCOM&nbsp;—<code>gedcom</code>
	</li>
	<li data-id="gettext">
		gettext&nbsp;—<code>gettext</code>, <code>po</code>
	</li>
	<li data-id="gherkin">
		Gherkin&nbsp;—<code>gherkin</code>
	</li>
	<li data-id="git">
		Git&nbsp;—<code>git</code>
	</li>
	<li data-id="glsl">
		GLSL&nbsp;—<code>glsl</code>
	</li>
	<li data-id="gn">
		GN&nbsp;—<code>gn</code>, <code>gni</code>
	</li>
	<li data-id="linker-script">
		GNU Linker Script&nbsp;—<code>linker-script</code>, <code>ld</code>
	</li>
	<li data-id="go">
		Go&nbsp;—<code>go</code>
	</li>
	<li data-id="go-module">
		Go module&nbsp;—<code>go-module</code>, <code>go-mod</code>
	</li>
	<li data-id="gradle">
		Gradle&nbsp;—<code>gradle</code>
	</li>
	<li data-id="graphql">
		GraphQL&nbsp;—<code>graphql</code>
	</li>
	<li data-id="groovy">
		Groovy&nbsp;—<code>groovy</code>
	</li>
	<li data-id="haml">
		Haml&nbsp;—<code>haml</code>
	</li>
	<li data-id="handlebars">
		Handlebars&nbsp;—<code>handlebars</code>, <code>hbs</code>, <code>mustache</code>
	</li>
	<li data-id="haskell">
		Haskell&nbsp;—<code>haskell</code>, <code>hs</code>
	</li>
	<li data-id="haxe">
		Haxe&nbsp;—<code>haxe</code>
	</li>
	<li data-id="hcl">
		HCL&nbsp;—<code>hcl</code>
	</li>
	<li data-id="hlsl">
		HLSL&nbsp;—<code>hlsl</code>
	</li>
	<li data-id="hoon">
		Hoon&nbsp;—<code>hoon</code>
	</li>
	<li data-id="http">
		HTTP&nbsp;—<code>http</code>
	</li>
	<li data-id="hpkp">
		HTTP Public-Key-Pins&nbsp;—<code>hpkp</code>
	</li>
	<li data-id="hsts">
		HTTP Strict-Transport-Security&nbsp;—<code>hsts</code>
	</li>
	<li data-id="ichigojam">
		IchigoJam&nbsp;—<code>ichigojam</code>
	</li>
	<li data-id="icon">
		Icon&nbsp;—<code>icon</code>
	</li>
	<li data-id="icu-message-format">
		ICU Message Format&nbsp;—<code>icu-message-format</code>
	</li>
	<li data-id="idris">
		Idris&nbsp;—<code>idris</code>, <code>idr</code>
	</li>
	<li data-id="ignore">
		.ignore&nbsp;—<code>ignore</code>, <code>gitignore</code>, <code>hgignore</code>, <code>npmignore</code>
	</li>
	<li data-id="inform7">
		Inform 7&nbsp;—<code>inform7</code>
	</li>
	<li data-id="ini">
		Ini&nbsp;—<code>ini</code>
	</li>
	<li data-id="io">
		Io&nbsp;—<code>io</code>
	</li>
	<li data-id="j">
		J&nbsp;—<code>j</code>
	</li>
	<li data-id="java">
		Java&nbsp;—<code>java</code>
	</li>
	<li data-id="javadoc">
		JavaDoc&nbsp;—<code>javadoc</code>
	</li>
	<li data-id="javadoclike">
		JavaDoc-like&nbsp;—<code>javadoclike</code>
	</li>
	<li data-id="javastacktrace">
		Java stack trace&nbsp;—<code>javastacktrace</code>
	</li>
	<li data-id="jexl">
		Jexl&nbsp;—<code>jexl</code>
	</li>
	<li data-id="jolie">
		Jolie&nbsp;—<code>jolie</code>
	</li>
	<li data-id="jq">
		JQ&nbsp;—<code>jq</code>
	</li>
	<li data-id="jsdoc">
		JSDoc&nbsp;—<code>jsdoc</code>
	</li>
	<li data-id="js-extras">
		JS Extras&nbsp;—<code>js-extras</code>
	</li>
	<li data-id="json">
		JSON&nbsp;—<code>json</code>, <code>webmanifest</code>
	</li>
	<li data-id="json5">
		JSON5&nbsp;—<code>json5</code>
	</li>
	<li data-id="jsonp">
		JSONP&nbsp;—<code>jsonp</code>
	</li>
	<li data-id="jsstacktrace">
		JS stack trace&nbsp;—<code>jsstacktrace</code>
	</li>
	<li data-id="js-templates">
		JS Templates&nbsp;—<code>js-templates</code>
	</li>
	<li data-id="julia">
		Julia&nbsp;—<code>julia</code>
	</li>
	<li data-id="keepalived">
		Keepalived Configure&nbsp;—<code>keepalived</code>
	</li>
	<li data-id="keyman">
		Keyman&nbsp;—<code>keyman</code>
	</li>
	<li data-id="kotlin">
		Kotlin&nbsp;—<code>kotlin</code>, <code>kt</code>, <code>kts</code>
	</li>
	<li data-id="kumir">
		KuMir (КуМир)&nbsp;—<code>kumir</code>, <code>kum</code>
	</li>
	<li data-id="kusto">
		Kusto&nbsp;—<code>kusto</code>
	</li>
	<li data-id="latex">
		LaTeX&nbsp;—<code>latex</code>, <code>tex</code>, <code>context</code>
	</li>
	<li data-id="latte">
		Latte&nbsp;—<code>latte</code>
	</li>
	<li data-id="less">
		Less&nbsp;—<code>less</code>
	</li>
	<li data-id="lilypond">
		LilyPond&nbsp;—<code>lilypond</code>, <code>ly</code>
	</li>
	<li data-id="liquid">
		Liquid&nbsp;—<code>liquid</code>
	</li>
	<li data-id="lisp">
		Lisp&nbsp;—<code>lisp</code>, <code>emacs</code>, <code>elisp</code>, <code>emacs-lisp</code>
	</li>
	<li data-id="livescript">
		LiveScript&nbsp;—<code>livescript</code>
	</li>
	<li data-id="llvm">
		LLVM IR&nbsp;—<code>llvm</code>
	</li>
	<li data-id="log">
		Log file&nbsp;—<code>log</code>
	</li>
	<li data-id="lolcode">
		LOLCODE&nbsp;—<code>lolcode</code>
	</li>
	<li data-id="lua">
		Lua&nbsp;—<code>lua</code>
	</li>
	<li data-id="magma">
		Magma (CAS)&nbsp;—<code>magma</code>
	</li>
	<li data-id="makefile">
		Makefile&nbsp;—<code>makefile</code>
	</li>
	<li data-id="markdown">
		Markdown&nbsp;—<code>markdown</code>, <code>md</code>
	</li>
	<li data-id="markup-templating">
		Markup templating&nbsp;—<code>markup-templating</code>
	</li>
	<li data-id="mata">
		Mata&nbsp;—<code>mata</code>
	</li>
	<li data-id="matlab">
		MATLAB&nbsp;—<code>matlab</code>
	</li>
	<li data-id="maxscript">
		MAXScript&nbsp;—<code>maxscript</code>
	</li>
	<li data-id="mel">
		MEL&nbsp;—<code>mel</code>
	</li>
	<li data-id="mermaid">
		Mermaid&nbsp;—<code>mermaid</code>
	</li>
	<li data-id="metafont">
		METAFONT&nbsp;—<code>metafont</code>
	</li>
	<li data-id="mizar">
		Mizar&nbsp;—<code>mizar</code>
	</li>
	<li data-id="mongodb">
		MongoDB&nbsp;—<code>mongodb</code>
	</li>
	<li data-id="monkey">
		Monkey&nbsp;—<code>monkey</code>
	</li>
	<li data-id="moonscript">
		MoonScript&nbsp;—<code>moonscript</code>, <code>moon</code>
	</li>
	<li data-id="n1ql">
		N1QL&nbsp;—<code>n1ql</code>
	</li>
	<li data-id="n4js">
		N4JS&nbsp;—<code>n4js</code>, <code>n4jsd</code>
	</li>
	<li data-id="nand2tetris-hdl">
		Nand To Tetris HDL&nbsp;—<code>nand2tetris-hdl</code>
	</li>
	<li data-id="naniscript">
		Naninovel Script&nbsp;—<code>naniscript</code>, <code>nani</code>
	</li>
	<li data-id="nasm">
		NASM&nbsp;—<code>nasm</code>
	</li>
	<li data-id="neon">
		NEON&nbsp;—<code>neon</code>
	</li>
	<li data-id="nevod">
		Nevod&nbsp;—<code>nevod</code>
	</li>
	<li data-id="nginx">
		nginx&nbsp;—<code>nginx</code>
	</li>
	<li data-id="nim">
		Nim&nbsp;—<code>nim</code>
	</li>
	<li data-id="nix">
		Nix&nbsp;—<code>nix</code>
	</li>
	<li data-id="nsis">
		NSIS&nbsp;—<code>nsis</code>
	</li>
	<li data-id="objectivec">
		Objective-C&nbsp;—<code>objectivec</code>, <code>objc</code>
	</li>
	<li data-id="ocaml">
		OCaml&nbsp;—<code>ocaml</code>
	</li>
	<li data-id="odin">
		Odin&nbsp;—<code>odin</code>
	</li>
	<li data-id="opencl">
		OpenCL&nbsp;—<code>opencl</code>
	</li>
	<li data-id="openqasm">
		OpenQasm&nbsp;—<code>openqasm</code>, <code>qasm</code>
	</li>
	<li data-id="oz">
		Oz&nbsp;—<code>oz</code>
	</li>
	<li data-id="parigp">
		PARI/GP&nbsp;—<code>parigp</code>
	</li>
	<li data-id="parser">
		Parser&nbsp;—<code>parser</code>
	</li>
	<li data-id="pascal">
		Pascal&nbsp;—<code>pascal</code>, <code>objectpascal</code>
	</li>
	<li data-id="pascaligo">
		Pascaligo&nbsp;—<code>pascaligo</code>
	</li>
	<li data-id="psl">
		PATROL Scripting Language&nbsp;—<code>psl</code>
	</li>
	<li data-id="pcaxis">
		PC-Axis&nbsp;—<code>pcaxis</code>, <code>px</code>
	</li>
	<li data-id="peoplecode">
		PeopleCode&nbsp;—<code>peoplecode</code>, <code>pcode</code>
	</li>
	<li data-id="perl">
		Perl&nbsp;—<code>perl</code>
	</li>
	<li data-id="php">
		PHP&nbsp;—<code>php</code>
	</li>
	<li data-id="phpdoc">
		PHPDoc&nbsp;—<code>phpdoc</code>
	</li>
	<li data-id="php-extras">
		PHP Extras&nbsp;—<code>php-extras</code>
	</li>
	<li data-id="plant-uml">
		PlantUML&nbsp;—<code>plant-uml</code>, <code>plantuml</code>
	</li>
	<li data-id="plsql">
		PL/SQL&nbsp;—<code>plsql</code>
	</li>
	<li data-id="powerquery">
		PowerQuery&nbsp;—<code>powerquery</code>, <code>pq</code>, <code>mscript</code>
	</li>
	<li data-id="powershell">
		PowerShell&nbsp;—<code>powershell</code>
	</li>
	<li data-id="processing">
		Processing&nbsp;—<code>processing</code>
	</li>
	<li data-id="prolog">
		Prolog&nbsp;—<code>prolog</code>
	</li>
	<li data-id="promql">
		PromQL&nbsp;—<code>promql</code>
	</li>
	<li data-id="properties">
		.properties&nbsp;—<code>properties</code>
	</li>
	<li data-id="protobuf">
		Protocol Buffers&nbsp;—<code>protobuf</code>
	</li>
	<li data-id="pug">
		Pug&nbsp;—<code>pug</code>
	</li>
	<li data-id="puppet">
		Puppet&nbsp;—<code>puppet</code>
	</li>
	<li data-id="pure">
		Pure&nbsp;—<code>pure</code>
	</li>
	<li data-id="purebasic">
		PureBasic&nbsp;—<code>purebasic</code>, <code>pbfasm</code>
	</li>
	<li data-id="purescript">
		PureScript&nbsp;—<code>purescript</code>, <code>purs</code>
	</li>
	<li data-id="python">
		Python&nbsp;—<code>python</code>, <code>py</code>
	</li>
	<li data-id="qsharp">
		Q#&nbsp;—<code>qsharp</code>, <code>qs</code>
	</li>
	<li data-id="q">
		Q (kdb+ database)&nbsp;—<code>q</code>
	</li>
	<li data-id="qml">
		QML&nbsp;—<code>qml</code>
	</li>
	<li data-id="qore">
		Qore&nbsp;—<code>qore</code>
	</li>
	<li data-id="r">
		R&nbsp;—<code>r</code>
	</li>
	<li data-id="racket">
		Racket&nbsp;—<code>racket</code>, <code>rkt</code>
	</li>
	<li data-id="cshtml">
		Razor C#&nbsp;—<code>cshtml</code>, <code>razor</code>
	</li>
	<li data-id="jsx">
		React JSX&nbsp;—<code>jsx</code>
	</li>
	<li data-id="tsx">
		React TSX&nbsp;—<code>tsx</code>
	</li>
	<li data-id="reason">
		Reason&nbsp;—<code>reason</code>
	</li>
	<li data-id="regex">
		Regex&nbsp;—<code>regex</code>
	</li>
	<li data-id="rego">
		Rego&nbsp;—<code>rego</code>
	</li>
	<li data-id="renpy">
		Ren&#39;py&nbsp;—<code>renpy</code>, <code>rpy</code>
	</li>
	<li data-id="rescript">
		ReScript&nbsp;—<code>rescript</code>, <code>res</code>
	</li>
	<li data-id="rest">
		reST (reStructuredText)&nbsp;—<code>rest</code>
	</li>
	<li data-id="rip">
		Rip&nbsp;—<code>rip</code>
	</li>
	<li data-id="roboconf">
		Roboconf&nbsp;—<code>roboconf</code>
	</li>
	<li data-id="robotframework">
		Robot Framework&nbsp;—<code>robotframework</code>, <code>robot</code>
	</li>
	<li data-id="ruby">
		Ruby&nbsp;—<code>ruby</code>, <code>rb</code>
	</li>
	<li data-id="rust">
		Rust&nbsp;—<code>rust</code>
	</li>
	<li data-id="sas">
		SAS&nbsp;—<code>sas</code>
	</li>
	<li data-id="sass">
		Sass (Sass)&nbsp;—<code>sass</code>
	</li>
	<li data-id="scss">
		Sass (SCSS)&nbsp;—<code>scss</code>
	</li>
	<li data-id="scala">
		Scala&nbsp;—<code>scala</code>
	</li>
	<li data-id="scheme">
		Scheme&nbsp;—<code>scheme</code>
	</li>
	<li data-id="shell-session">
		Shell session&nbsp;—<code>shell-session</code>, <code>sh-session</code>, <code>shellsession</code>
	</li>
	<li data-id="smali">
		Smali&nbsp;—<code>smali</code>
	</li>
	<li data-id="smalltalk">
		Smalltalk&nbsp;—<code>smalltalk</code>
	</li>
	<li data-id="smarty">
		Smarty&nbsp;—<code>smarty</code>
	</li>
	<li data-id="sml">
		SML&nbsp;—<code>sml</code>, <code>smlnj</code>
	</li>
	<li data-id="solidity">
		Solidity (Ethereum)&nbsp;—<code>solidity</code>, <code>sol</code>
	</li>
	<li data-id="solution-file">
		Solution file&nbsp;—<code>solution-file</code>, <code>sln</code>
	</li>
	<li data-id="soy">
		Soy (Closure Template)&nbsp;—<code>soy</code>
	</li>
	<li data-id="sparql">
		SPARQL&nbsp;—<code>sparql</code>, <code>rq</code>
	</li>
	<li data-id="splunk-spl">
		Splunk SPL&nbsp;—<code>splunk-spl</code>
	</li>
	<li data-id="sqf">
		SQF: Status Quo Function (Arma 3)&nbsp;—<code>sqf</code>
	</li>
	<li data-id="sql">
		SQL&nbsp;—<code>sql</code>
	</li>
	<li data-id="squirrel">
		Squirrel&nbsp;—<code>squirrel</code>
	</li>
	<li data-id="stan">
		Stan&nbsp;—<code>stan</code>
	</li>
	<li data-id="stata">
		Stata Ado&nbsp;—<code>stata</code>
	</li>
	<li data-id="iecst">
		Structured Text (IEC 61131-3)&nbsp;—<code>iecst</code>
	</li>
	<li data-id="stylus">
		Stylus&nbsp;—<code>stylus</code>
	</li>
	<li data-id="supercollider">
		SuperCollider&nbsp;—<code>supercollider</code>, <code>sclang</code>
	</li>
	<li data-id="swift">
		Swift&nbsp;—<code>swift</code>
	</li>
	<li data-id="systemd">
		Systemd configuration file&nbsp;—<code>systemd</code>
	</li>
	<li data-id="t4-templating">
		T4 templating&nbsp;—<code>t4-templating</code>
	</li>
	<li data-id="t4-cs">
		T4 Text Templates (C#)&nbsp;—<code>t4-cs</code>, <code>t4</code>
	</li>
	<li data-id="t4-vb">
		T4 Text Templates (VB)&nbsp;—<code>t4-vb</code>
	</li>
	<li data-id="tap">
		TAP&nbsp;—<code>tap</code>
	</li>
	<li data-id="tcl">
		Tcl&nbsp;—<code>tcl</code>
	</li>
	<li data-id="tt2">
		Template Toolkit 2&nbsp;—<code>tt2</code>
	</li>
	<li data-id="textile">
		Textile&nbsp;—<code>textile</code>
	</li>
	<li data-id="toml">
		TOML&nbsp;—<code>toml</code>
	</li>
	<li data-id="tremor">
		Tremor&nbsp;—<code>tremor</code>, <code>trickle</code>, <code>troy</code>
	</li>
	<li data-id="turtle">
		Turtle&nbsp;—<code>turtle</code>, <code>trig</code>
	</li>
	<li data-id="twig">
		Twig&nbsp;—<code>twig</code>
	</li>
	<li data-id="typescript">
		TypeScript&nbsp;—<code>typescript</code>, <code>ts</code>
	</li>
	<li data-id="typoscript">
		TypoScript&nbsp;—<code>typoscript</code>, <code>tsconfig</code>
	</li>
	<li data-id="unrealscript">
		UnrealScript&nbsp;—<code>unrealscript</code>, <code>uscript</code>, <code>uc</code>
	</li>
	<li data-id="uorazor">
		UO Razor Script&nbsp;—<code>uorazor</code>
	</li>
	<li data-id="uri">
		URI&nbsp;—<code>uri</code>, <code>url</code>
	</li>
	<li data-id="v">
		V&nbsp;—<code>v</code>
	</li>
	<li data-id="vala">
		Vala&nbsp;—<code>vala</code>
	</li>
	<li data-id="vbnet">
		VB.Net&nbsp;—<code>vbnet</code>
	</li>
	<li data-id="velocity">
		Velocity&nbsp;—<code>velocity</code>
	</li>
	<li data-id="verilog">
		Verilog&nbsp;—<code>verilog</code>
	</li>
	<li data-id="vhdl">
		VHDL&nbsp;—<code>vhdl</code>
	</li>
	<li data-id="vim">
		vim&nbsp;—<code>vim</code>
	</li>
	<li data-id="visual-basic">
		Visual Basic&nbsp;—<code>visual-basic</code>, <code>vb</code>, <code>vba</code>
	</li>
	<li data-id="warpscript">
		WarpScript&nbsp;—<code>warpscript</code>
	</li>
	<li data-id="wasm">
		WebAssembly&nbsp;—<code>wasm</code>
	</li>
	<li data-id="web-idl">
		Web IDL&nbsp;—<code>web-idl</code>, <code>webidl</code>
	</li>
	<li data-id="wgsl">
		WGSL&nbsp;—<code>wgsl</code>
	</li>
	<li data-id="wiki">
		Wiki markup&nbsp;—<code>wiki</code>
	</li>
	<li data-id="wolfram">
		Wolfram language&nbsp;—<code>wolfram</code>, <code>mathematica</code>, <code>nb</code>, <code>wl</code>
	</li>
	<li data-id="wren">
		Wren&nbsp;—<code>wren</code>
	</li>
	<li data-id="xeora">
		Xeora&nbsp;—<code>xeora</code>, <code>xeoracube</code>
	</li>
	<li data-id="xml-doc">
		XML doc (.net)&nbsp;—<code>xml-doc</code>
	</li>
	<li data-id="xojo">
		Xojo (REALbasic)&nbsp;—<code>xojo</code>
	</li>
	<li data-id="xquery">
		XQuery&nbsp;—<code>xquery</code>
	</li>
	<li data-id="yaml">
		YAML&nbsp;—<code>yaml</code>, <code>yml</code>
	</li>
	<li data-id="yang">
		YANG&nbsp;—<code>yang</code>
	</li>
	<li data-id="zig">
		Zig&nbsp;—<code>zig</code>
	</li>
</ul>
<p>Couldn’t find the language you were looking for? <a href="https://github.com/PrismJS/prism/issues">Request it</a>!</p>
</section>
<section>
<h1 id="plugins" tabindex="-1"><a class="header-anchor" href="#plugins">Plugins</a></h1>
<p>Plugins are additional scripts (and CSS code) that extend Prism’s functionality. Many of the following plugins are official, but are released as plugins to keep the Prism Core small for those who don’t need the extra functionality.</p>
<ul class="plugin-list">
	<li>
		<a href="plugins/autolinker">Autolinker</a>
		<div>Converts URLs and emails in code to clickable links. Parses Markdown links in comments.</div>
	</li>
	<li>
		<a href="plugins/autoloader">Autoloader</a>
		<div>Automatically loads the needed languages to highlight the code blocks.</div>
	</li>
	<li>
		<a href="plugins/command-line">Command Line</a>
		<div>Display a command line with a prompt and, optionally, the output/response from the commands.</div>
	</li>
	<li>
		<a href="plugins/copy-to-clipboard">Copy to Clipboard</a>
		<div>Add a button that copies the code block to the clipboard when clicked.</div>
	</li>
	<li>
		<a href="plugins/custom-class">Custom Class</a>
		<div>This plugin allows you to prefix Prism’s default classes (<code>.comment</code> can become <code>.namespace--comment</code>) or replace them with your defined ones (like <code>.editor__comment</code>). You can even add new classes.</div>
	</li>
	<li>
		<a href="plugins/data-uri-highlight">Data URI Highlight</a>
		<div>Highlights data-URI contents.</div>
	</li>
	<li>
		<a href="plugins/diff-highlight">Diff Highlight</a>
		<div>Highlight the code inside diff blocks.</div>
	</li>
	<li>
		<a href="plugins/download-button">Download Button</a>
		<div>A button in the toolbar of a code block adding a convenient way to download a code file.</div>
	</li>
	<li>
		<a href="plugins/file-highlight">File Highlight</a>
		<div>Fetch external files and highlight them with Prism. Used on the Prism website itself.</div>
	</li>
	<li>
		<a href="plugins/filter-highlight-all">Filter highlightAll</a>
		<div>Filters the elements the <code>highlightAll</code> and <code>highlightAllUnder</code> methods actually highlight.</div>
	</li>
	<li>
		<a href="plugins/highlight-keywords">Highlight Keywords</a>
		<div>Adds special CSS classes for each keyword for fine-grained highlighting.</div>
	</li>
	<li>
		<a href="plugins/inline-color">Inline Color</a>
		<div>Adds a small inline preview for colors in style sheets.</div>
	</li>
	<li>
		<a href="plugins/jsonp-highlight">JSONP Highlight</a>
		<div>Fetch content with JSONP and highlight some interesting content (e.g. GitHub/Gists or Bitbucket API).</div>
	</li>
	<li>
		<a href="plugins/keep-markup">Keep Markup</a>
		<div>Prevents custom markup from being dropped out during highlighting.</div>
	</li>
	<li>
		<a href="plugins/line-highlight">Line Highlight</a>
		<div>Highlights specific lines and/or line ranges.</div>
	</li>
	<li>
		<a href="plugins/line-numbers">Line Numbers</a>
		<div>Line number at the beginning of code lines.</div>
	</li>
	<li>
		<a href="plugins/match-braces">Match braces</a>
		<div>Highlights matching braces.</div>
	</li>
	<li>
		<a href="plugins/normalize-whitespace">Normalize Whitespace</a>
		<div>Supports multiple operations to normalize whitespace in code blocks.</div>
	</li>
	<li>
		<a href="plugins/previewers">Previewers</a>
		<div>Previewers for angles, colors, gradients, easing and time.</div>
	</li>
	<li>
		<a href="plugins/remove-initial-line-feed">Remove Initial Line Feed</a>
		<div>Removes the initial line feed in code blocks.</div>
	</li>
	<li>
		<a href="plugins/show-invisibles">Show Invisibles</a>
		<div>Show hidden characters such as tabs and line breaks.</div>
	</li>
	<li>
		<a href="plugins/show-language">Show Language</a>
		<div>Display the highlighted language in code blocks (inline code does not show the label).</div>
	</li>
	<li>
		<a href="plugins/toolbar">Toolbar</a>
		<div>Attach a toolbar for plugins to easily register buttons on the top of a code block.</div>
	</li>
	<li>
		<a href="plugins/treeview">Treeview</a>
		<div>A language with special styles to highlight file system tree structures.</div>
	</li>
	<li>
		<a href="plugins/unescaped-markup">Unescaped Markup</a>
		<div>Write markup without having to escape anything.</div>
	</li>
	<li>
		<a href="plugins/wpd">WebPlatform Docs</a>
		<div>Makes tokens link to <a href="https://webplatform.github.io/docs/">WebPlatform.org documentation</a>. The links open in a new tab.</div>
	</li>
	</ul>
<p>No assembly required to use them. Just select them in the <a href='/download'>download</a> page.</p>
<p>It’s very easy to <a href='/extending#writing-plugins'>write your own Prism plugins</a>. Did you write a plugin for Prism that you want added to this list? <a href="https://github.com/PrismJS/plugins/">Send a pull request</a>!</p>
</section>
<section>
<h1 id="third-party-language-definitions" tabindex="-1"><a class="header-anchor" href="#third-party-language-definitions">Third-party language definitions</a></h1>
<ul>
<li><a href="https://github.com/SassDoc/prism-scss-sassdoc">SassDoc Sass/Scss comments</a></li>
<li><a href="https://github.com/Liquibase/prism-liquibase">Liquibase CLI Bash language extension</a></li>
</ul>
</section>
<section>
<h1 id="third-party-tutorials" tabindex="-1"><a class="header-anchor" href="#third-party-tutorials">Third-party tutorials</a></h1>
<p>Several tutorials have been written by members of the community to help you integrate Prism into multiple different website types and configurations:</p>
<ul>
<li><a href="https://startblogging101.com/how-to-add-prism-js-syntax-highlighting-wordpress/">How to Add Prism.js Syntax Highlighting to Your WordPress Site</a></li>
<li><a href="https://websitebeaver.com/escape-html-inside-code-or-pre-tag-to-entities-to-display-raw-code-with-prismjs">Escape HTML Inside <code>&lt;code&gt;</code> or <code>&lt;pre&gt;</code> Tag to Entities to Display Raw Code with PrismJS</a></li>
<li><a href="http://wp.tutsplus.com/tutorials/plugins/adding-a-syntax-highlighter-shortcode-using-prism-js/">Adding a Syntax Highlighter Shortcode Using Prism.js | WPTuts+</a></li>
<li><a href="https://www.stramaxon.com/2012/07/prism-syntax-highlighter-for-blogger.html">Implement PrismJs Syntax Highlighting to your Blogger/BlogSpot</a></li>
<li><a href="https://schier.co/blog/2013/01/07/how-to-re-run-prismjs-on-ajax-content.html">How To Re-Run Prism.js On AJAX Content</a></li>
<li><a href="https://www.semisedlak.com/highlight-your-code-syntax-with-prismjs">Highlight your code syntax with Prism.js</a></li>
<li><a href="https://usetypo3.com/fs-code-snippet.html">A code snippet content element powered by Prism.js for TYPO3 CMS</a></li>
</ul>
<!-- - [Code syntax highlighting with Angular and Prism.js](https://auralinna.blog/post/2017/code-syntax-highlighting-with-angular-and-prismjs) -->
<ul>
<li><a href="https://mkaz.blog/wordpress/code-syntax-highlighting-in-gutenberg/">Code syntax highlighting in Gutenberg, WordPress block editor</a></li>
<li><a href="https://karlkaufmann.com/writing/technotes/code-highlighting-prism-drupal">Code Highlighting with Prism.js in Drupal</a></li>
<li><a href="https://betterstack.dev/blog/code-highlighting-in-react-using-prismjs/">Code highlighting in React using Prism.js</a></li>
</ul>
<!-- - [Using Prism.js in React Native](https://www.akashmittal.com/react-native-prismjs-using-webview/) -->
<ul>
<li><a href="https://itsmycode.com/prismjs-tutorial/">PrismJS Tutorial | Implement Prism in HTML and React</a></li>
<li>Code syntax highlighting in Pug with <a href="https://webdiscus.github.io/pug-loader/pug-filters/highlight.html">:highlight</a> and <a href="https://webdiscus.github.io/pug-loader/pug-filters/markdown.html">:markdown</a> filters using <a href="https://github.com/webdiscus/pug-loader">pug-loader</a> and Prism.js</li>
</ul>
<p>Please note that the tutorials listed here are not verified to contain correct information. Read at your risk and always check the official documentation here if something doesn’t work. 🙂</p>
<p>Have you written a tutorial about Prism that’s not already included here? Send a pull request!</p>
</section>
<section>
<h1 id="credits" tabindex="-1"><a class="header-anchor" href="#credits">Credits</a></h1>
<ul>
<li>Special thanks to <a href="https://github.com/RunDevelopment">Michael Schmidt</a>, <a href="https://github.com/mAAdhaTTah">James DiGioia</a>, <a href="https://github.com/Golmote">Golmote</a> and <a href="https://github.com/apfelbox">Jannik Zschiesche</a> for their contributions and for being <strong>amazing maintainers</strong>. Prism would not have been able to keep up without their help.</li>
<li>To <a href="https://twitter.com/kizmarh">Roman Komarov</a> for his contributions, feedback and testing.</li>
<li>To <a href="https://twitter.com/zdfs">Zachary Forrest</a> for <a href="https://twitter.com/zdfs/statuses/217834980871639041">coming up with the name “Prism”</a>.</li>
<li>To <a href="https://stellarr.deviantart.com/">stellarr</a> for the <a href="https://stellarr.deviantart.com/art/Spectra-Wallpaper-Pack-97785901">spectrum background</a> used on this page.</li>
<li>To <a href="https://twitter.com/thecodezombie">Jason Hobbs</a> for <a href="https://twitter.com/thecodezombie/status/217663703825399809">encouraging me</a> to release this script as standalone.</li>
</ul>
</section>

	</main>

	<footer>
		<img id="logo" src="https://lea.verou.me/logo.svg" />
		<p>Handcrafted with &hearts;, by
			<a href="https://lea.verou.me" target="_blank">Lea Verou</a>,
			<a href="https://github.com/Golmote" target="_blank">Golmote</a>,
			<a href="https://github.com/mAAdhaTTah" target="_blank">James DiGioia</a>,
			<a href="https://github.com/RunDevelopment" target="_blank">Michael Schmidt</a>
			&amp; <a href="https://github.com/PrismJS/prism/graphs/contributors" target="_blank">all these awesome people</a>
		</p>
		<nav>
			<ul>
				<li><a href="/">Home</a></li>
				<li><a href='/download'>Download</a></li>
				<li><a href='/faq'>FAQ</a></li>
				<li><a href='/test'>Test drive</a></li>
				<li><a href='/extending'>API docs</a></li>
				<li><a href="https://github.com/PrismJS/prism/">Fork Prism on GitHub</a></li>
				<li><a href="https://x.com/prismjs">Follow Prism on X</a></li>
			</ul>
		</nav>
	</footer>

	<script src="https://dev.prismjs.com/prism.js"></script>
	<script src="/assets/theme-switcher.js" type="module"></script>
	<script src="plugins/keep-markup/prism-keep-markup.js" ></script>
	<script src="https://dev.prismjs.com/components/prism-bash.js" ></script>
</body>
</html>
This page’s logo (SVG), highlighted with Prism:

<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 200 170">
	<path fill="#fff" d="M55.37 131.5H48.4v9.13h6.97c1.67 0 2.92-.4 3.78-1.22.85
	-.8 1.28-1.92 1.28-3.33s-.43-2.54-1.28-3.35c-.86-.8-2.12-1.2-3.78-1.2m29.52
	6.4c.3-.53.47-1.2.47-2.04 0-1.35-.45-2.4-1.37-3.2-.92-.76-2.14-1.15-3.65
	-1.15H72.9v8.52h7.32c2.26 0 3.82-.7 4.67-2.1M100 0L0 170h200L100 0M60.86
	141.03c-1.3 1.22-3.1 1.84-5.33 1.84H48.4v7.55H46v-21.2h9.53c2.24 0 4.02.63
	5.34 1.87 1.3 1.23 1.96 2.88 1.96 4.95 0 2.1-.66 3.75-1.97 4.98m24.5 9.4l
	-5.1-8.14h-7.37v8.12h-2.4v-21.2h10.14c2.15 0 3.88.6 5.18 1.8 1.3 1.18 1.95
	2.8 1.95 4.84 0 2.64-1.1 4.44-3.3 5.4-.6.28-1.22.5-1.82.6l5.57 8.56h-2.85m
	13.43 0h-2.4v-21.2h2.4v21.2m23.56-1.32c-1.48 1.05-3.53 1.57-6.16 1.57-2.96 0
	-5.23-.6-6.78-1.85-1.4-1.1-2.18-2.7-2.37-4.74h2.5c.08 1.45.78 2.56 2.1 3.33
	1.16.67 2.68 1 4.58 1 3.97 0 5.95-1.25 5.95-3.74 0-.86-.35-1.53-1.07-2.02-.7
	-.5-1.6-.9-2.68-1.2-1.07-.33-2.24-.63-3.48-.9s-2.4-.65-3.5-1.08-1.97-1.02
	-2.68-1.73c-.7-.72-1.07-1.68-1.07-2.9 0-1.73.65-3.13 1.97-4.22 1.32-1.08
	3.32-1.62 6-1.62 2.67 0 4.75.6 6.23 1.85 1.34 1.1 2.05 2.5 2.14 4.2h-2.46c
	-.22-1.76-1.35-2.92-3.4-3.5-.72-.2-1.62-.3-2.7-.3s-1.98.1-2.72.35c-.74.25
	-1.3.55-1.7.9-.42.35-.7.74-.83 1.17s-.2.88-.2 1.36c0 .5.2.93.62 1.33s.96.75
	1.65 1.03c.68.28 1.46.52 2.33.73.88.2 1.77.43 2.67.65.9.22 1.8.48 2.68.77.87
	.3 1.65.65 2.33 1.1 1.53.96 2.28 2.27 2.28 3.94 0 2-.74 3.5-2.22 4.55m28.84
	1.32v-17.54l-7.84 10.08-7.97-10.08v17.54H133v-21.2h2.78l7.58 10.06 7.45
	-10.05h2.8v21.2h-2.4"/>
</svg>
If you’re still not sold, you can view more examples or try it out for yourself.

Full list of features
Only 2KB minified & gzipped (core). Each language definition adds roughly 300-500 bytes.
Encourages good author practices. Other highlighters encourage or even force you to use elements that are semantically wrong, like <pre> (on its own) or <script>. Prism forces you to use the correct element for marking up code: <code>. On its own for inline code, or inside a <pre> for blocks of code. In addition, the language is defined through the way recommended in the HTML5 draft: through a language-xxxx class.
The language-xxxx class is inherited. This means that if multiple code snippets have the same language, you can just define it once, in one of their common ancestors.
Supports parallelism with Web Workers, if available. Disabled by default (why?).
Very easy to extend without modifying the code, due to Prism’s plugin architecture. Multiple hooks are scattered throughout the source.
Very easy to define new languages. The only thing you need is a good understanding of regular expressions.
All styling is done through CSS, with sensible class names rather than ugly, namespaced, abbreviated nonsense.
Wide browser support: Edge, IE11, Firefox, Chrome, Safari, Opera, most mobile browsers.
Highlights embedded languages (e.g. CSS inside HTML, JavaScript inside HTML).
Highlights inline code as well, not just code blocks.
It doesn’t force you to use any Prism-specific markup, not even a Prism-specific class name, only standard markup you should be using anyway. So, you can just try it for a while, remove it if you don’t like it and leave no traces behind.
Highlight specific lines and/or line ranges (requires plugin).
Show invisible characters like tabs, line breaks etc (requires plugin).
Autolink URLs and emails, use Markdown links in comments (requires plugin).
Limitations
Any pre-existing HTML in the code will be stripped off. There are ways around it though.
Regex-based so it *will* fail on certain edge cases, which are documented in the known failures page.
Some of our themes have problems with certain layouts. Known cases are documented here.
No IE 6-10 support. If someone can read code, they are probably in the 95% of the population with a modern browser.
Basic usage
You will need to include the prism.css and prism.js files you downloaded in your page. Example:

<!DOCTYPE html>
<html>
<head>
	...
	<link href="themes/prism.css" rel="stylesheet" />
</head>
<body>
	...
	<script src="prism.js"></script>
</body>
</html>
Prism does its best to encourage good authoring practices. Therefore, it only works with <code> elements, since marking up code without a <code> element is semantically invalid. According to the HTML5 spec, the recommended way to define a code language is a language-xxxx class, which is what Prism uses. Alternatively, Prism also supports a shorter version: lang-xxxx.

The recommended way to mark up a code block (both for semantics and for Prism) is a <pre> element with a <code> element inside, like so:

<pre><code class="language-css">p { color: red }</code></pre>
If you use that pattern, the <pre> will automatically get the language-xxxx class (if it doesn’t already have it) and will be styled as a code block.

Inline code snippets are done like this:

<code class="language-css">p { color: red }</code>
Note: You have to escape all < and & characters inside <code> elements (code blocks and inline snippets) with &lt; and &amp; respectively, or else the browser might interpret them as an HTML tag or entity. If you have large portions of HTML code, you can use the Unescaped Markup plugin to work around this.

Language inheritance
To make things easier however, Prism assumes that the language class is inherited. Therefore, if multiple <code> elements have the same language, you can add the language-xxxx class on one of their common ancestors. This way, you can also define a document-wide default language, by adding a language-xxxx class on the <body> or <html> element.

If you want to opt-out of highlighting a <code> element that inherits its language, you can add the language-none class to it. The none language can also be inherited to disable highlighting for the element with the class and all of its descendants.

If you want to opt-out of highlighting but still use plugins like Show Invisibles, use language-plain class instead.

Manual highlighting
If you want to prevent any elements from being automatically highlighted and instead use the API, you can set Prism.manual to true before the DOMContentLoaded event is fired. By setting the data-manual attribute on the <script> element containing Prism core, this will be done automatically. Example:

<script src="prism.js" data-manual></script>
or

<script>
window.Prism = window.Prism || {};
window.Prism.manual = true;
</script>
<script src="prism.js"></script>
Usage with CDNs
In combination with CDNs, we recommend using the Autoloader plugin which automatically loads languages when necessary.

The setup of the Autoloader, will look like the following. You can also add your own themes of course.

<!DOCTYPE html>
<html>
<head>
	...
	<link href="https:///prismjs@v1.x/themes/prism.css" rel="stylesheet" />
</head>
<body>
	...
	<script src="https:///prismjs@v1.x/components/prism-core.min.js"></script>
<script src="https:///prismjs@v1.x/plugins/autoloader/prism-autoloader.min.js"></script>
</body>
</html>
Please note that links in the above code sample serve as placeholders. You have to replace them with valid links to the CDN of your choice.

CDNs which provide PrismJS are e.g. cdnjs, jsDelivr, and UNPKG.

Usage with Webpack, Browserify, & Other Bundlers
If you want to use Prism with a bundler, install Prism with npm:

$ npm install prismjs
You can then import into your bundle:

import Prism from 'prismjs';
To make it easy to configure your Prism instance with only the languages and plugins you need, use the babel plugin, babel-plugin-prismjs. This will allow you to load the minimum number of languages and plugins to satisfy your needs. See that plugin’s documentation for configuration details.

Usage with Node
If you want to use Prism on the server or through the command line, Prism can be used with Node.js as well. This might be useful if you’re trying to generate static HTML pages with highlighted code for environments that don’t support browser-side JS, like AMP pages.

Example:

const Prism = require('prismjs');

// The code snippet you want to highlight, as a string
const code = `var data = 1;`;

// Returns a highlighted HTML string
const html = Prism.highlight(code, Prism.languages.javascript, 'javascript');
Requiring prismjs will load the default languages: markup, css, clike and javascript. You can load more languages with the loadLanguages() utility, which will automatically handle any required dependencies.

Example:

const Prism = require('prismjs');
const loadLanguages = require('prismjs/components/');
loadLanguages(['haml']);

// The code snippet you want to highlight, as a string
const code = `= ['hi', 'there', 'reader!'].join " "`;

// Returns a highlighted HTML string
const html = Prism.highlight(code, Prism.languages.haml, 'haml');
Note: Do not use loadLanguages() with Webpack or another bundler, as this will cause Webpack to include all languages and plugins. Use the babel plugin described above.

Note: loadLanguages() will ignore unknown languages and log warning messages to the console. You can prevent the warnings by setting loadLanguages.silent = true.








---Plugins:

Inline Color
Adds a small inline preview for colors in style sheets.

On this page
Examples
CSS
HTML (Markup)
Examples
CSS
span.foo {
	background-color: 
navy;
	color: 
#BFD;
}

span.bar {
	background: 
rgba(105, 0, 12, .38);
	color: 
hsl(30, 100%, 50%);
	border-color: 
transparent;
}
/**
 * prism.js default theme for JavaScript, CSS and HTML
 * Based on dabblet (http://dabblet.com)
 * @author Lea Verou
 */

code[class*="language-"],
pre[class*="language-"] {
	color: 
black;
	background: none;
	text-shadow: 0 1px 
white;
	font-family: Consolas, Monaco, 'Andale Mono', 'Ubuntu Mono', monospace;
	font-size: 1em;
	text-align: left;
	white-space: pre;
	word-spacing: normal;
	word-break: normal;
	word-wrap: normal;
	line-height: 1.5;

	-moz-tab-size: 4;
	-o-tab-size: 4;
	tab-size: 4;

	-webkit-hyphens: none;
	-moz-hyphens: none;
	-ms-hyphens: none;
	hyphens: none;
}

pre[class*="language-"]::-moz-selection, pre[class*="language-"] ::-moz-selection,
code[class*="language-"]::-moz-selection, code[class*="language-"] ::-moz-selection {
	text-shadow: none;
	background: 
#b3d4fc;
}

pre[class*="language-"]::selection, pre[class*="language-"] ::selection,
code[class*="language-"]::selection, code[class*="language-"] ::selection {
	text-shadow: none;
	background: 
#b3d4fc;
}

@media print {
	code[class*="language-"],
	pre[class*="language-"] {
		text-shadow: none;
	}
}

/* Code blocks */
pre[class*="language-"] {
	padding: 1em;
	margin: .5em 0;
	overflow: auto;
}

:not(pre) > code[class*="language-"],
pre[class*="language-"] {
	background: 
#f5f2f0;
}

/* Inline code */
:not(pre) > code[class*="language-"] {
	padding: .1em;
	border-radius: .3em;
	white-space: normal;
}

.token.comment,
.token.prolog,
.token.doctype,
.token.cdata {
	color: 
slategray;
}

.token.punctuation {
	color: 
#999;
}

.token.namespace {
	opacity: .7;
}

.token.property,
.token.tag,
.token.boolean,
.token.number,
.token.constant,
.token.symbol,
.token.deleted {
	color: 
#905;
}

.token.selector,
.token.attr-name,
.token.string,
.token.char,
.token.builtin,
.token.inserted {
	color: 
#690;
}

.token.operator,
.token.entity,
.token.url,
.language-css .token.string,
.style .token.string {
	color: 
#9a6e3a;
	/* This background color was intended by the author of this theme. */
	background: 
hsla(0, 0%, 100%, .5);
}

.token.atrule,
.token.attr-value,
.token.keyword {
	color: 
#07a;
}

.token.function,
.token.class-name {
	color: 
#DD4A68;
}

.token.regex,
.token.important,
.token.variable {
	color: 
#e90;
}

.token.important,
.token.bold {
	font-weight: bold;
}
.token.italic {
	font-style: italic;
}

.token.entity {
	cursor: help;
}
HTML (Markup)
<!DOCTYPE html>
<html lang="en">
<head>

<meta charset="utf-8" />
<title>Example</title>
<style>
	/* Also works here */
	a.not-a-class {
		color: 
red;
	}
</style>
<body style="color: 
black">

</body>
</html>





Line Numbers
Line number at the beginning of code lines.

On this page
How to use
Examples
JavaScript
CSS
HTML
Unknown languages
Soft wrap support
How to use
Obviously, this is supposed to work only for code blocks (<pre><code>) and not for inline code.

Add the line-numbers class to your desired <pre> or any of its ancestors, and the Line Numbers plugin will take care of the rest. To give all code blocks line numbers, add the line-numbers class to the <body> of the page. This is part of a general activation mechanism where adding the line-numbers (or no-line-numbers) class to any element will enable (or disable) the Line Numbers plugin for all code blocks in that element.
Example:

<body class="line-numbers"> <!-- enabled for the whole page -->

	<!-- with line numbers -->
	<pre><code>...</code></pre>
	<!-- disabled for a specific element - without line numbers -->
	<pre class="no-line-numbers"><code>...</code></pre>

	<div class="no-line-numbers"> <!-- disabled for this subtree -->

		<!-- without line numbers -->
		<pre><code>...</code></pre>
		<!-- enabled for a specific element - with line numbers -->
		<pre class="line-numbers"><code>...</code></pre>

	</div>
</body>
Optional: You can specify the data-start (Number) attribute on the <pre> element. It will shift the line counter.

Optional: To support multiline line numbers using soft wrap, apply the CSS white-space: pre-line; or white-space: pre-wrap; to your desired <pre>.

Examples
JavaScript
(function () {

	if (typeof Prism === 'undefined' || typeof document === 'undefined') {
		return;
	}

	/**
	 * Plugin name which is used as a class name for <pre> which is activating the plugin
	 *
	 * @type {string}
	 */
	var PLUGIN_NAME = 'line-numbers';

	/**
	 * Regular expression used for determining line breaks
	 *
	 * @type {RegExp}
	 */
	var NEW_LINE_EXP = /\n(?!$)/g;


	/**
	 * Global exports
	 */
	var config = Prism.plugins.lineNumbers = {
		/**
		 * Get node for provided line number
		 *
		 * @param {Element} element pre element
		 * @param {number} number line number
		 * @returns {Element|undefined}
		 */
		getLine: function (element, number) {
			if (element.tagName !== 'PRE' || !element.classList.contains(PLUGIN_NAME)) {
				return;
			}

			var lineNumberRows = element.querySelector('.line-numbers-rows');
			if (!lineNumberRows) {
				return;
			}
			var lineNumberStart = parseInt(element.getAttribute('data-start'), 10) || 1;
			var lineNumberEnd = lineNumberStart + (lineNumberRows.children.length - 1);

			if (number < lineNumberStart) {
				number = lineNumberStart;
			}
			if (number > lineNumberEnd) {
				number = lineNumberEnd;
			}

			var lineIndex = number - lineNumberStart;

			return lineNumberRows.children[lineIndex];
		},

		/**
		 * Resizes the line numbers of the given element.
		 *
		 * This function will not add line numbers. It will only resize existing ones.
		 *
		 * @param {HTMLElement} element A `<pre>` element with line numbers.
		 * @returns {void}
		 */
		resize: function (element) {
			resizeElements([element]);
		},

		/**
		 * Whether the plugin can assume that the units font sizes and margins are not depended on the size of
		 * the current viewport.
		 *
		 * Setting this to `true` will allow the plugin to do certain optimizations for better performance.
		 *
		 * Set this to `false` if you use any of the following CSS units: `vh`, `vw`, `vmin`, `vmax`.
		 *
		 * @type {boolean}
		 */
		assumeViewportIndependence: true
	};

	/**
	 * Resizes the given elements.
	 *
	 * @param {HTMLElement[]} elements
	 */
	function resizeElements(elements) {
		elements = elements.filter(function (e) {
			var codeStyles = getStyles(e);
			var whiteSpace = codeStyles['white-space'];
			return whiteSpace === 'pre-wrap' || whiteSpace === 'pre-line';
		});

		if (elements.length == 0) {
			return;
		}

		var infos = elements.map(function (element) {
			var codeElement = element.querySelector('code');
			var lineNumbersWrapper = element.querySelector('.line-numbers-rows');
			if (!codeElement || !lineNumbersWrapper) {
				return undefined;
			}

			/** @type {HTMLElement} */
			var lineNumberSizer = element.querySelector('.line-numbers-sizer');
			var codeLines = codeElement.textContent.split(NEW_LINE_EXP);

			if (!lineNumberSizer) {
				lineNumberSizer = document.createElement('span');
				lineNumberSizer.className = 'line-numbers-sizer';

				codeElement.appendChild(lineNumberSizer);
			}

			lineNumberSizer.innerHTML = '0';
			lineNumberSizer.style.display = 'block';

			var oneLinerHeight = lineNumberSizer.getBoundingClientRect().height;
			lineNumberSizer.innerHTML = '';

			return {
				element: element,
				lines: codeLines,
				lineHeights: [],
				oneLinerHeight: oneLinerHeight,
				sizer: lineNumberSizer,
			};
		}).filter(Boolean);

		infos.forEach(function (info) {
			var lineNumberSizer = info.sizer;
			var lines = info.lines;
			var lineHeights = info.lineHeights;
			var oneLinerHeight = info.oneLinerHeight;

			lineHeights[lines.length - 1] = undefined;
			lines.forEach(function (line, index) {
				if (line && line.length > 1) {
					var e = lineNumberSizer.appendChild(document.createElement('span'));
					e.style.display = 'block';
					e.textContent = line;
				} else {
					lineHeights[index] = oneLinerHeight;
				}
			});
		});

		infos.forEach(function (info) {
			var lineNumberSizer = info.sizer;
			var lineHeights = info.lineHeights;

			var childIndex = 0;
			for (var i = 0; i < lineHeights.length; i++) {
				if (lineHeights[i] === undefined) {
					lineHeights[i] = lineNumberSizer.children[childIndex++].getBoundingClientRect().height;
				}
			}
		});

		infos.forEach(function (info) {
			var lineNumberSizer = info.sizer;
			var wrapper = info.element.querySelector('.line-numbers-rows');

			lineNumberSizer.style.display = 'none';
			lineNumberSizer.innerHTML = '';

			info.lineHeights.forEach(function (height, lineNumber) {
				wrapper.children[lineNumber].style.height = height + 'px';
			});
		});
	}

	/**
	 * Returns style declarations for the element
	 *
	 * @param {Element} element
	 */
	function getStyles(element) {
		if (!element) {
			return null;
		}

		return window.getComputedStyle ? getComputedStyle(element) : (element.currentStyle || null);
	}

	var lastWidth = undefined;
	window.addEventListener('resize', function () {
		if (config.assumeViewportIndependence && lastWidth === window.innerWidth) {
			return;
		}
		lastWidth = window.innerWidth;

		resizeElements(Array.prototype.slice.call(document.querySelectorAll('pre.' + PLUGIN_NAME)));
	});

	Prism.hooks.add('complete', function (env) {
		if (!env.code) {
			return;
		}

		var code = /** @type {Element} */ (env.element);
		var pre = /** @type {HTMLElement} */ (code.parentNode);

		// works only for <code> wrapped inside <pre> (not inline)
		if (!pre || !/pre/i.test(pre.nodeName)) {
			return;
		}

		// Abort if line numbers already exists
		if (code.querySelector('.line-numbers-rows')) {
			return;
		}

		// only add line numbers if <code> or one of its ancestors has the `line-numbers` class
		if (!Prism.util.isActive(code, PLUGIN_NAME)) {
			return;
		}

		// Remove the class 'line-numbers' from the <code>
		code.classList.remove(PLUGIN_NAME);
		// Add the class 'line-numbers' to the <pre>
		pre.classList.add(PLUGIN_NAME);

		var match = env.code.match(NEW_LINE_EXP);
		var linesNum = match ? match.length + 1 : 1;
		var lineNumbersWrapper;

		var lines = new Array(linesNum + 1).join('<span></span>');

		lineNumbersWrapper = document.createElement('span');
		lineNumbersWrapper.setAttribute('aria-hidden', 'true');
		lineNumbersWrapper.className = 'line-numbers-rows';
		lineNumbersWrapper.innerHTML = lines;

		if (pre.hasAttribute('data-start')) {
			pre.style.counterReset = 'linenumber ' + (parseInt(pre.getAttribute('data-start'), 10) - 1);
		}

		env.element.appendChild(lineNumbersWrapper);

		resizeElements([pre]);

		Prism.hooks.run('line-numbers', env);
	});

	Prism.hooks.add('line-numbers', function (env) {
		env.plugins = env.plugins || {};
		env.plugins.lineNumbers = true;
	});

}());
CSS
Please note that this <pre> does not have the line-numbers class but its parent does.

pre[class*="language-"].line-numbers {
	position: relative;
	padding-left: 3.8em;
	counter-reset: linenumber;
}

pre[class*="language-"].line-numbers > code {
	position: relative;
	white-space: inherit;
}

.line-numbers .line-numbers-rows {
	position: absolute;
	pointer-events: none;
	top: 0;
	font-size: 100%;
	left: -3.8em;
	width: 3em; /* works for line-numbers below 1000 lines */
	letter-spacing: -1px;
	border-right: 1px solid #999;

	-webkit-user-select: none;
	-moz-user-select: none;
	-ms-user-select: none;
	user-select: none;

}

	.line-numbers-rows > span {
		display: block;
		counter-increment: linenumber;
	}

		.line-numbers-rows > span:before {
			content: counter(linenumber);
			color: #999;
			display: block;
			padding-right: 0.8em;
			text-align: right;
		}
HTML
Please note the data-start="-5" in the code below.

<!DOCTYPE html>
<html lang="en"
	data-page="/plugins/line-numbers/"
	data-inputpath="plugins/line-numbers/README.md">
<head>
	<title>
		Line Numbers ▲ Prism 
	</title>
	<meta name="viewport" content="width=device-width" />
	<meta charset="utf-8" />
	<link rel="icon" href="/assets/logo.svg" />
	<link rel="stylesheet" href="/assets/style.css" />
	<link rel="stylesheet" href="https://dev.prismjs.com/themes/prism.css" />
	<script>var _gaq = [["_setAccount", "UA-33746269-1"], ["_trackPageview"]];</script>
	<script src="https://www.google-analytics.com/ga.js" async></script>

	</head>

<body class="">
	<header>
		<div class="intro">
			<h1><a href="/"><img src="/assets/logo.svg" alt="Prism" /> </a></h1>

			<a class='download-button' href='/download'>Download</a>

			<p>
				Prism is a lightweight, extensible syntax highlighter, built with modern web standards in mind.
				It’s used in millions of websites, including some of those you visit daily.
			</p>
		</div>

		<div id="theme">
			<p>Theme:</p>
			<input type="radio" name="theme" id="theme-prism" value="prism" />
			<label for="theme-prism">Default</label>
			<input type="radio" name="theme" id="theme-prism-dark" value="prism-dark" />
			<label for="theme-prism-dark">Dark</label>
			<input type="radio" name="theme" id="theme-prism-funky" value="prism-funky" />
			<label for="theme-prism-funky">Funky</label>
			<input type="radio" name="theme" id="theme-prism-okaidia" value="prism-okaidia" />
			<label for="theme-prism-okaidia">Okaidia</label>
			<input type="radio" name="theme" id="theme-prism-twilight" value="prism-twilight" />
			<label for="theme-prism-twilight">Twilight</label>
			<input type="radio" name="theme" id="theme-prism-coy" value="prism-coy" />
			<label for="theme-prism-coy">Coy</label>
			<input type="radio" name="theme" id="theme-prism-solarizedlight" value="prism-solarizedlight" />
			<label for="theme-prism-solarizedlight">Solarized Light</label>
			<input type="radio" name="theme" id="theme-prism-tomorrow" value="prism-tomorrow" />
			<label for="theme-prism-tomorrow">Tomorrow Night</label>
		</div>

		<h2>Line Numbers</h2>
		<p>Line number at the beginning of code lines.</p>
	</header>

	<aside id="toc">
		<h2>On this page</h2>
		<nav class="toc" >
        <ul><li><a href="#how-to-use">How to use</a></li><li><a href="#examples">Examples</a><ul><li><a href="#javascript">JavaScript</a></li><li><a href="#css">CSS</a></li><li><a href="#html">HTML</a></li><li><a href="#unknown-languages">Unknown languages</a></li><li><a href="#soft-wrap-support">Soft wrap support</a></li></ul></li></ul>
      </nav>
	</aside>

	<main>
		<section class="language-markup">
<h1 id="how-to-use" tabindex="-1"><a class="header-anchor" href="#how-to-use">How to use</a></h1>
<p>Obviously, this is supposed to work only for code blocks (<code>&lt;pre&gt;&lt;code&gt;</code>) and not for inline code.</p>
<p>Add the <code>line-numbers</code> class to your desired <code>&lt;pre&gt;</code> or any of its ancestors, and the Line Numbers plugin will take care of the rest. To give all code blocks line numbers, add the <code>line-numbers</code> class to the <code>&lt;body&gt;</code> of the page. This is part of a general activation mechanism where adding the <code>line-numbers</code> (or <code>no-line-numbers</code>) class to any element will enable (or disable) the Line Numbers plugin for all code blocks in that element.<br>
Example:</p>
<pre ><code class="language-html">&lt;body class=&quot;line-numbers&quot;&gt; &lt;!-- enabled for the whole page --&gt;

	&lt;!-- with line numbers --&gt;
	&lt;pre&gt;&lt;code&gt;...&lt;/code&gt;&lt;/pre&gt;
	&lt;!-- disabled for a specific element - without line numbers --&gt;
	&lt;pre class=&quot;no-line-numbers&quot;&gt;&lt;code&gt;...&lt;/code&gt;&lt;/pre&gt;

	&lt;div class=&quot;no-line-numbers&quot;&gt; &lt;!-- disabled for this subtree --&gt;

		&lt;!-- without line numbers --&gt;
		&lt;pre&gt;&lt;code&gt;...&lt;/code&gt;&lt;/pre&gt;
		&lt;!-- enabled for a specific element - with line numbers --&gt;
		&lt;pre class=&quot;line-numbers&quot;&gt;&lt;code&gt;...&lt;/code&gt;&lt;/pre&gt;

	&lt;/div&gt;
&lt;/body&gt;</code></pre><p>Optional: You can specify the <code>data-start</code> (Number) attribute on the <code>&lt;pre&gt;</code> element. It will shift the line counter.</p>
<p>Optional: To support multiline line numbers using soft wrap, apply the CSS <code>white-space: pre-line;</code> or <code>white-space: pre-wrap;</code> to your desired <code>&lt;pre&gt;</code>.</p>
</section>
<section class="line-numbers language-none">
<h1 id="examples" tabindex="-1"><a class="header-anchor" href="#examples">Examples</a></h1>
<h2 id="javascript" tabindex="-1"><a class="header-anchor" href="#javascript">JavaScript</a></h2>
<pre class="line-numbers" data-src="./prism-line-numbers.js"></pre>
<h2 id="css" tabindex="-1"><a class="header-anchor" href="#css">CSS</a></h2>
<p>Please note that this <code>&lt;pre&gt;</code> does not have the <code>line-numbers</code> class but its parent does.</p>
<pre data-src="./prism-line-numbers.css"></pre>
<h2 id="html" tabindex="-1"><a class="header-anchor" href="#html">HTML</a></h2>
<p>Please note the <code>data-start=&quot;-5&quot;</code> in the code below.</p>
<pre class="line-numbers" data-src="./index.html" data-start="-5"></pre>
<h2 id="unknown-languages" tabindex="-1"><a class="header-anchor" href="#unknown-languages">Unknown languages</a></h2>
<pre  class="language-none line-numbers"><code >This raw text
is not highlighted
but it still has
line numbers</code></pre><h2 id="soft-wrap-support" tabindex="-1"><a class="header-anchor" href="#soft-wrap-support">Soft wrap support</a></h2>
<p>Please note the <code>style=&quot;white-space: pre-wrap;&quot;</code> in the code below.</p>
<pre class="line-numbers" data-src="./index.html" data-start="-5" style="white-space: pre-wrap;"></pre>
</section>

	</main>

	<footer>
		<img id="logo" src="https://lea.verou.me/logo.svg" />
		<p>Handcrafted with &hearts;, by
			<a href="https://lea.verou.me" target="_blank">Lea Verou</a>,
			<a href="https://github.com/Golmote" target="_blank">Golmote</a>,
			<a href="https://github.com/mAAdhaTTah" target="_blank">James DiGioia</a>,
			<a href="https://github.com/RunDevelopment" target="_blank">Michael Schmidt</a>
			&amp; <a href="https://github.com/PrismJS/prism/graphs/contributors" target="_blank">all these awesome people</a>
		</p>
		<nav>
			<ul>
				<li><a href="/">Home</a></li>
				<li><a href='/download'>Download</a></li>
				<li><a href='/faq'>FAQ</a></li>
				<li><a href='/test'>Test drive</a></li>
				<li><a href='/extending'>API docs</a></li>
				<li><a href="https://github.com/PrismJS/prism/">Fork Prism on GitHub</a></li>
				<li><a href="https://x.com/prismjs">Follow Prism on X</a></li>
			</ul>
		</nav>
	</footer>

	<script src="https://dev.prismjs.com/prism.js"></script>
	<script src="/assets/theme-switcher.js" type="module"></script>
	
	<script src="./prism-line-numbers.js" ></script>
	<link rel="stylesheet" href="./prism-line-numbers.css"  />
</body>
</html>
Unknown languages
This raw text
is not highlighted
but it still has
line numbers
Soft wrap support
Please note the style="white-space: pre-wrap;" in the code below.

<!DOCTYPE html>
<html lang="en"
	data-page="/plugins/line-numbers/"
	data-inputpath="plugins/line-numbers/README.md">
<head>
	<title>
		Line Numbers ▲ Prism 
	</title>
	<meta name="viewport" content="width=device-width" />
	<meta charset="utf-8" />
	<link rel="icon" href="/assets/logo.svg" />
	<link rel="stylesheet" href="/assets/style.css" />
	<link rel="stylesheet" href="https://dev.prismjs.com/themes/prism.css" />
	<script>var _gaq = [["_setAccount", "UA-33746269-1"], ["_trackPageview"]];</script>
	<script src="https://www.google-analytics.com/ga.js" async></script>

	</head>

<body class="">
	<header>
		<div class="intro">
			<h1><a href="/"><img src="/assets/logo.svg" alt="Prism" /> </a></h1>

			<a class='download-button' href='/download'>Download</a>

			<p>
				Prism is a lightweight, extensible syntax highlighter, built with modern web standards in mind.
				It’s used in millions of websites, including some of those you visit daily.
			</p>
		</div>

		<div id="theme">
			<p>Theme:</p>
			<input type="radio" name="theme" id="theme-prism" value="prism" />
			<label for="theme-prism">Default</label>
			<input type="radio" name="theme" id="theme-prism-dark" value="prism-dark" />
			<label for="theme-prism-dark">Dark</label>
			<input type="radio" name="theme" id="theme-prism-funky" value="prism-funky" />
			<label for="theme-prism-funky">Funky</label>
			<input type="radio" name="theme" id="theme-prism-okaidia" value="prism-okaidia" />
			<label for="theme-prism-okaidia">Okaidia</label>
			<input type="radio" name="theme" id="theme-prism-twilight" value="prism-twilight" />
			<label for="theme-prism-twilight">Twilight</label>
			<input type="radio" name="theme" id="theme-prism-coy" value="prism-coy" />
			<label for="theme-prism-coy">Coy</label>
			<input type="radio" name="theme" id="theme-prism-solarizedlight" value="prism-solarizedlight" />
			<label for="theme-prism-solarizedlight">Solarized Light</label>
			<input type="radio" name="theme" id="theme-prism-tomorrow" value="prism-tomorrow" />
			<label for="theme-prism-tomorrow">Tomorrow Night</label>
		</div>

		<h2>Line Numbers</h2>
		<p>Line number at the beginning of code lines.</p>
	</header>

	<aside id="toc">
		<h2>On this page</h2>
		<nav class="toc" >
        <ul><li><a href="#how-to-use">How to use</a></li><li><a href="#examples">Examples</a><ul><li><a href="#javascript">JavaScript</a></li><li><a href="#css">CSS</a></li><li><a href="#html">HTML</a></li><li><a href="#unknown-languages">Unknown languages</a></li><li><a href="#soft-wrap-support">Soft wrap support</a></li></ul></li></ul>
      </nav>
	</aside>

	<main>
		<section class="language-markup">
<h1 id="how-to-use" tabindex="-1"><a class="header-anchor" href="#how-to-use">How to use</a></h1>
<p>Obviously, this is supposed to work only for code blocks (<code>&lt;pre&gt;&lt;code&gt;</code>) and not for inline code.</p>
<p>Add the <code>line-numbers</code> class to your desired <code>&lt;pre&gt;</code> or any of its ancestors, and the Line Numbers plugin will take care of the rest. To give all code blocks line numbers, add the <code>line-numbers</code> class to the <code>&lt;body&gt;</code> of the page. This is part of a general activation mechanism where adding the <code>line-numbers</code> (or <code>no-line-numbers</code>) class to any element will enable (or disable) the Line Numbers plugin for all code blocks in that element.<br>
Example:</p>
<pre ><code class="language-html">&lt;body class=&quot;line-numbers&quot;&gt; &lt;!-- enabled for the whole page --&gt;

	&lt;!-- with line numbers --&gt;
	&lt;pre&gt;&lt;code&gt;...&lt;/code&gt;&lt;/pre&gt;
	&lt;!-- disabled for a specific element - without line numbers --&gt;
	&lt;pre class=&quot;no-line-numbers&quot;&gt;&lt;code&gt;...&lt;/code&gt;&lt;/pre&gt;

	&lt;div class=&quot;no-line-numbers&quot;&gt; &lt;!-- disabled for this subtree --&gt;

		&lt;!-- without line numbers --&gt;
		&lt;pre&gt;&lt;code&gt;...&lt;/code&gt;&lt;/pre&gt;
		&lt;!-- enabled for a specific element - with line numbers --&gt;
		&lt;pre class=&quot;line-numbers&quot;&gt;&lt;code&gt;...&lt;/code&gt;&lt;/pre&gt;

	&lt;/div&gt;
&lt;/body&gt;</code></pre><p>Optional: You can specify the <code>data-start</code> (Number) attribute on the <code>&lt;pre&gt;</code> element. It will shift the line counter.</p>
<p>Optional: To support multiline line numbers using soft wrap, apply the CSS <code>white-space: pre-line;</code> or <code>white-space: pre-wrap;</code> to your desired <code>&lt;pre&gt;</code>.</p>
</section>
<section class="line-numbers language-none">
<h1 id="examples" tabindex="-1"><a class="header-anchor" href="#examples">Examples</a></h1>
<h2 id="javascript" tabindex="-1"><a class="header-anchor" href="#javascript">JavaScript</a></h2>
<pre class="line-numbers" data-src="./prism-line-numbers.js"></pre>
<h2 id="css" tabindex="-1"><a class="header-anchor" href="#css">CSS</a></h2>
<p>Please note that this <code>&lt;pre&gt;</code> does not have the <code>line-numbers</code> class but its parent does.</p>
<pre data-src="./prism-line-numbers.css"></pre>
<h2 id="html" tabindex="-1"><a class="header-anchor" href="#html">HTML</a></h2>
<p>Please note the <code>data-start=&quot;-5&quot;</code> in the code below.</p>
<pre class="line-numbers" data-src="./index.html" data-start="-5"></pre>
<h2 id="unknown-languages" tabindex="-1"><a class="header-anchor" href="#unknown-languages">Unknown languages</a></h2>
<pre  class="language-none line-numbers"><code >This raw text
is not highlighted
but it still has
line numbers</code></pre><h2 id="soft-wrap-support" tabindex="-1"><a class="header-anchor" href="#soft-wrap-support">Soft wrap support</a></h2>
<p>Please note the <code>style=&quot;white-space: pre-wrap;&quot;</code> in the code below.</p>
<pre class="line-numbers" data-src="./index.html" data-start="-5" style="white-space: pre-wrap;"></pre>
</section>

	</main>

	<footer>
		<img id="logo" src="https://lea.verou.me/logo.svg" />
		<p>Handcrafted with &hearts;, by
			<a href="https://lea.verou.me" target="_blank">Lea Verou</a>,
			<a href="https://github.com/Golmote" target="_blank">Golmote</a>,
			<a href="https://github.com/mAAdhaTTah" target="_blank">James DiGioia</a>,
			<a href="https://github.com/RunDevelopment" target="_blank">Michael Schmidt</a>
			&amp; <a href="https://github.com/PrismJS/prism/graphs/contributors" target="_blank">all these awesome people</a>
		</p>
		<nav>
			<ul>
				<li><a href="/">Home</a></li>
				<li><a href='/download'>Download</a></li>
				<li><a href='/faq'>FAQ</a></li>
				<li><a href='/test'>Test drive</a></li>
				<li><a href='/extending'>API docs</a></li>
				<li><a href="https://github.com/PrismJS/prism/">Fork Prism on GitHub</a></li>
				<li><a href="https://x.com/prismjs">Follow Prism on X</a></li>
			</ul>
		</nav>
	</footer>

	<script src="https://dev.prismjs.com/prism.js"></script>
	<script src="/assets/theme-switcher.js" type="module"></script>
	
	<script src="./prism-line-numbers.js" ></script>
	<link rel="stylesheet" href="./prism-line-numbers.css"  />
</body>
</html>





Toolbar
Attach a toolbar for plugins to easily register buttons on the top of a code block.

On this page
How to use
Registering buttons
Ordering buttons
How to use
The Toolbar plugin allows for several methods to register your button, using the Prism.plugins.toolbar.registerButton function.

The simplest method is through the HTML API. Add a data-label attribute to the pre element, and the Toolbar plugin will read the value of that attribute and append a label to the code snippet.

<pre data-src="./prism-toolbar.js" data-label="Hello World!"></pre>
Hello World!
If you want to provide arbitrary HTML to the label, create a template element with the HTML you want in the label, and provide the template element’s id to data-label. The Toolbar plugin will use the template’s content for the button. You can also use to declare your event handlers inline:

<pre data-src="./prism-toolbar.js" data-label="my-label-button"></pre>
<template id="my-label-button"><button onclick="console.log('This is an inline-handler');">My button</button></template>
Registering buttons
For more flexibility, the Toolbar exposes a JavaScript function that can be used to register new buttons or labels to the Toolbar, Prism.plugins.toolbar.registerButton.

The function accepts a key for the button and an object with a text property string and an optional onClick function or a url string. The onClick function will be called when the button is clicked, while the url property will be set to the anchor tag’s href.

Prism.plugins.toolbar.registerButton("hello-world", {
	text: "Hello World!", // required
	onClick: function (env) {
		// optional
		alert(`This code snippet is written in ${env.language}.`);
	},
});
See how the above code registers the Hello World! button? You can use this in your plugins to register your own buttons with the toolbar.

If you need more control, you can provide a function to registerButton that returns either a span, a, or button element.

Prism.plugins.toolbar.registerButton("select-code", env => {
	let button = document.createElement("button");
	button.innerHTML = "Select Code";

	button.addEventListener("click", () => {
		// Source: http://stackoverflow.com/a/11128179/2757940
		if (document.body.createTextRange) {
			// ms
			let range = document.body.createTextRange();
			range.moveToElementText(env.element);
			range.select();
		}
		else if (window.getSelection) {
			// moz, opera, webkit
			let selection = window.getSelection();
			let range = document.createRange();
			range.selectNodeContents(env.element);
			selection.removeAllRanges();
			selection.addRange(range);
		}
	});

	return button;
});
The above function creates the Select Code button you see, and when you click it, the code gets highlighted.

Ordering buttons
By default, the buttons will be added to the code snippet in the order they were registered. If more control over the order is needed, the data-toolbar-order attribute can be used. Given a comma-separated list of button names, it will ensure that these buttons will be displayed in the given order.
Buttons not listed will not be displayed. This means that buttons can be disabled using this technique.

Example: The “Hello World!” button will appear before the “Select Code” button and the custom label button will not be displayed.

<pre data-toolbar-order="hello-world,select-code" data-label="Hello World!"><code></code></pre>
The data-toolbar-order attribute is inherited, so you can define the button order for the whole document by adding the attribute to the body of the page.

<body data-toolbar-order="select-code,hello-world,label">






Download Button
A button in the toolbar of a code block adding a convenient way to download a code file.

On this page
How to use
Examples
How to use
Use the data-src and data-download-link attribute on a <pre> elements similar to Autoloader, like so:

<pre data-src="myfile.js" data-download-link></pre>
Optionally, the text of the button can also be customized by using a data-download-link-label attribute.

<pre data-src="myfile.js" data-download-link data-download-link-label="Download this file"></pre>
Examples
The plugin’s JS code:

(function () {

	if (typeof Prism === 'undefined' || typeof document === 'undefined' || !document.querySelector) {
		return;
	}

	Prism.plugins.toolbar.registerButton('download-file', function (env) {
		var pre = env.element.parentNode;
		if (!pre || !/pre/i.test(pre.nodeName) || !pre.hasAttribute('data-src') || !pre.hasAttribute('data-download-link')) {
			return;
		}
		var src = pre.getAttribute('data-src');
		var a = document.createElement('a');
		a.textContent = pre.getAttribute('data-download-link-label') || 'Download';
		a.setAttribute('download', '');
		a.href = src;
		return a;
	});

}());
Download the code!
This page:

<!DOCTYPE html>
<html lang="en"
	data-page="/plugins/download-button/"
	data-inputpath="plugins/download-button/README.md">
<head>
	<title>
		Download Button ▲ Prism 
	</title>
	<meta name="viewport" content="width=device-width" />
	<meta charset="utf-8" />
	<link rel="icon" href="/assets/logo.svg" />
	<link rel="stylesheet" href="/assets/style.css" />
	<link rel="stylesheet" href="https://dev.prismjs.com/themes/prism.css" />
	<script>var _gaq = [["_setAccount", "UA-33746269-1"], ["_trackPageview"]];</script>
	<script src="https://www.google-analytics.com/ga.js" async></script>

	</head>

<body class="">
	<header>
		<div class="intro">
			<h1><a href="/"><img src="/assets/logo.svg" alt="Prism" /> </a></h1>

			<a class='download-button' href='/download'>Download</a>

			<p>
				Prism is a lightweight, extensible syntax highlighter, built with modern web standards in mind.
				It’s used in millions of websites, including some of those you visit daily.
			</p>
		</div>

		<div id="theme">
			<p>Theme:</p>
			<input type="radio" name="theme" id="theme-prism" value="prism" />
			<label for="theme-prism">Default</label>
			<input type="radio" name="theme" id="theme-prism-dark" value="prism-dark" />
			<label for="theme-prism-dark">Dark</label>
			<input type="radio" name="theme" id="theme-prism-funky" value="prism-funky" />
			<label for="theme-prism-funky">Funky</label>
			<input type="radio" name="theme" id="theme-prism-okaidia" value="prism-okaidia" />
			<label for="theme-prism-okaidia">Okaidia</label>
			<input type="radio" name="theme" id="theme-prism-twilight" value="prism-twilight" />
			<label for="theme-prism-twilight">Twilight</label>
			<input type="radio" name="theme" id="theme-prism-coy" value="prism-coy" />
			<label for="theme-prism-coy">Coy</label>
			<input type="radio" name="theme" id="theme-prism-solarizedlight" value="prism-solarizedlight" />
			<label for="theme-prism-solarizedlight">Solarized Light</label>
			<input type="radio" name="theme" id="theme-prism-tomorrow" value="prism-tomorrow" />
			<label for="theme-prism-tomorrow">Tomorrow Night</label>
		</div>

		<h2>Download Button</h2>
		<p>A button in the toolbar of a code block adding a convenient way to download a code file.</p>
	</header>

	<aside id="toc">
		<h2>On this page</h2>
		<nav class="toc" >
        <ul><li><a href="#how-to-use">How to use</a></li><li><a href="#examples">Examples</a></li></ul>
      </nav>
	</aside>

	<main>
		<section class="language-markup">
<h1 id="how-to-use" tabindex="-1"><a class="header-anchor" href="#how-to-use">How to use</a></h1>
<p>Use the <code>data-src</code> and <code>data-download-link</code> attribute on a <code>&lt;pre&gt;</code> elements similar to <a href="../autoloader">Autoloader</a>, like so:</p>
<pre ><code class="language-html">&lt;pre data-src=&quot;myfile.js&quot; data-download-link&gt;&lt;/pre&gt;</code></pre><p>Optionally, the text of the button can also be customized by using a <code>data-download-link-label</code> attribute.</p>
<pre ><code class="language-html">&lt;pre data-src=&quot;myfile.js&quot; data-download-link data-download-link-label=&quot;Download this file&quot;&gt;&lt;/pre&gt;</code></pre></section>
<section>
<h1 id="examples" tabindex="-1"><a class="header-anchor" href="#examples">Examples</a></h1>
<p>The plugin’s JS code:</p>
<pre data-src="./prism-download-button.js" data-download-link data-download-link-label="Download the code!"></pre>
<p>This page:</p>
<pre data-src="./index.html" data-download-link></pre>
</section>

	</main>

	<footer>
		<img id="logo" src="https://lea.verou.me/logo.svg" />
		<p>Handcrafted with &hearts;, by
			<a href="https://lea.verou.me" target="_blank">Lea Verou</a>,
			<a href="https://github.com/Golmote" target="_blank">Golmote</a>,
			<a href="https://github.com/mAAdhaTTah" target="_blank">James DiGioia</a>,
			<a href="https://github.com/RunDevelopment" target="_blank">Michael Schmidt</a>
			&amp; <a href="https://github.com/PrismJS/prism/graphs/contributors" target="_blank">all these awesome people</a>
		</p>
		<nav>
			<ul>
				<li><a href="/">Home</a></li>
				<li><a href='/download'>Download</a></li>
				<li><a href='/faq'>FAQ</a></li>
				<li><a href='/test'>Test drive</a></li>
				<li><a href='/extending'>API docs</a></li>
				<li><a href="https://github.com/PrismJS/prism/">Fork Prism on GitHub</a></li>
				<li><a href="https://x.com/prismjs">Follow Prism on X</a></li>
			</ul>
		</nav>
	</footer>

	<script src="https://dev.prismjs.com/prism.js"></script>
	<script src="/assets/theme-switcher.js" type="module"></script>
	<link rel="stylesheet" href="../toolbar/prism-toolbar.css"  />
	<script src="../toolbar/prism-toolbar.js" ></script>
	<script src="./prism-download-button.js" ></script>
</body>
</html>
Download

